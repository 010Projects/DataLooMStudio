using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

using DeletionEligibilityEvaluation = DataLooMStudio.Modules.Retention.DeletionEligibilityEvaluation;
using DeletionEligibilityPolicy = DataLooMStudio.Modules.Retention.DeletionEligibilityPolicy;
using DeletionEligibilityPolicyInput = DataLooMStudio.Modules.Retention.DeletionEligibilityPolicyInput;
using LegalHold = DataLooMStudio.Modules.Retention.LegalHold;
using LegalHoldReleaseRequest = DataLooMStudio.Modules.Retention.LegalHoldReleaseRequest;
using LegalHoldReleaseStates = DataLooMStudio.Modules.Retention.LegalHoldReleaseStates;
using RetentionPolicy = DataLooMStudio.Modules.Retention.RetentionPolicy;

namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed partial class RetentionGovernanceService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    IClock clock,
    IProductAuthorityService productAuthorityService,
    IOutboxWriter outboxWriter,
    PostgresRlsSessionContext rlsSessionContext,
    IEvidenceDisposalObjectStore disposalObjectStore) : IRetentionGovernanceService
{
    private static readonly Regex PolicyKeyRegex = new("^[A-Za-z0-9._:-]{3,128}$", RegexOptions.Compiled);

    private static readonly Regex IdempotencyRegex = new("^[A-Za-z0-9._:-]{8,128}$", RegexOptions.Compiled);

    public async Task<RetentionPolicyResult> DefineRetentionPolicyAsync(
        RetentionPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ValidatePolicy(command);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for retention governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var policyKey = command.PolicyKey.Trim();
        var idempotencyKey = NormalizePolicyIdempotencyKey(command);
        var requestHash = ComputePolicyRequestHash(command);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await RequireProductPermissionAsync(
                actor,
                ProductAuthorityPermissions.ManageRetentionPolicy,
                ProductAuthorityResourceTypes.GovernanceRetention,
                policyKey,
                ProductAuthorityCapabilities.GovernanceRetention,
                ProductAuthorityActions.RetentionPolicyManage,
                ProductAuthorityRoleNames.RetentionAdministrator,
                cancellationToken);

            var existingByIdempotency = await dbContext.RetentionPolicies
                .SingleOrDefaultAsync(policy => policy.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByIdempotency is not null)
            {
                if (!existingByIdempotency.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different retention policy request.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToResult(existingByIdempotency, IdempotentReplay: true);
            }

            var existingPolicy = await dbContext.RetentionPolicies
                .SingleOrDefaultAsync(policy => policy.PolicyKey == policyKey, cancellationToken);
            if (existingPolicy is not null)
            {
                throw new RetentionGovernanceConflictException("A retention policy already exists for this policy key.");
            }

            var policy = new RetentionPolicy
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                PolicyKey = policyKey,
                Description = command.Description.Trim(),
                RetainForDays = command.RetainForDays,
                LegalHoldOverridesDeletion = command.LegalHoldOverridesDeletion,
                CreatedBy = actor,
                CreatedAt = now,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };
            dbContext.RetentionPolicies.Add(policy);
            AddAudit(
                context,
                actor,
                "Retention.PolicyDefined",
                "RetentionPolicy",
                policy.PolicyKey,
                $"retention-policy:{policy.Id}",
                "Succeeded",
                now,
                new
                {
                    policy.PolicyKey,
                    policy.RetainForDays,
                    policy.LegalHoldOverridesDeletion,
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });

            await AddOutboxAsync(
                context,
                "RetentionPolicyDefined",
                now,
                new
                {
                    eventVersion = 1,
                    policyId = policy.Id.ToString("D"),
                    policyKey = policy.PolicyKey,
                    aggregateId = policy.PolicyKey,
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResult(policy, IdempotentReplay: false);
        });
    }

    public async Task<LegalHoldResult> PlaceLegalHoldAsync(
        PlaceLegalHoldCommand command,
        CancellationToken cancellationToken)
    {
        ValidateLegalHold(command);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for retention governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeLegalHoldIdempotencyKey(command);
        var requestHash = ComputeLegalHoldRequestHash(command);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var evidence = await dbContext.EvidenceRecords
                .SingleOrDefaultAsync(item => item.Id == command.EvidenceId, cancellationToken);
            if (evidence is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            await RequireProductPermissionAsync(
                actor,
                ProductAuthorityPermissions.ManageLegalHold,
                ProductAuthorityResourceTypes.GovernanceLegalHold,
                command.EvidenceId.ToString(),
                ProductAuthorityCapabilities.GovernanceLegalHold,
                ProductAuthorityActions.LegalHoldManage,
                ProductAuthorityRoleNames.LegalHoldAdministrator,
                cancellationToken);

            var existingByIdempotency = await dbContext.LegalHolds
                .SingleOrDefaultAsync(hold => hold.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByIdempotency is not null)
            {
                if (!existingByIdempotency.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different legal hold request.");
                }

                await transaction.CommitAsync(cancellationToken);
                return new LegalHoldResult(
                    existingByIdempotency.Id,
                    existingByIdempotency.EvidenceId,
                    existingByIdempotency.PlacedAt,
                    EvidenceUnderLegalHold: true,
                    IdempotentReplay: true);
            }

            var activeHoldExists = await dbContext.LegalHolds.AnyAsync(
                hold => hold.EvidenceId == command.EvidenceId && !hold.ReleasedAt.HasValue,
                cancellationToken);
            if (activeHoldExists)
            {
                throw new RetentionGovernanceConflictException("An active legal hold already exists for this Evidence record.");
            }

            var legalHold = new LegalHold
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                EvidenceId = command.EvidenceId,
                Reason = command.Reason.Trim(),
                PlacedBy = actor,
                PlacedAt = now,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };
            evidence.IsUnderLegalHold = true;
            evidence.ConcurrencyToken = Guid.NewGuid();
            dbContext.LegalHolds.Add(legalHold);

            var causationId = $"legal-hold:{legalHold.Id}";
            AddAudit(
                context,
                actor,
                "Retention.LegalHoldPlaced",
                "Evidence",
                command.EvidenceId.ToString(),
                causationId,
                "Succeeded",
                now,
                new
                {
                    legalHoldId = legalHold.Id,
                    reasonHash = Hash(command.Reason.Trim()),
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });
            await AddLineageAsync(
                context,
                actor,
                evidence.LineageId,
                "LegalHoldPlaced",
                causationId,
                now,
                cancellationToken);

            await AddOutboxAsync(
                context,
                "LegalHoldPlaced",
                now,
                new
                {
                    eventVersion = 1,
                    legalHoldId = legalHold.Id.ToString("D"),
                    evidenceId = command.EvidenceId.ToString(),
                    lineageId = evidence.LineageId.ToString(),
                    aggregateId = command.EvidenceId.ToString(),
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new LegalHoldResult(
                legalHold.Id,
                legalHold.EvidenceId,
                legalHold.PlacedAt,
                EvidenceUnderLegalHold: true,
                IdempotentReplay: false);
        });
    }

    public async Task<LegalHoldReleaseRequestResult> RequestLegalHoldReleaseAsync(
        LegalHoldReleaseRequestCommand command,
        CancellationToken cancellationToken)
    {
        ValidateReleaseRequest(command);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for retention governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeReleaseRequestIdempotencyKey(command);
        var requestHash = ComputeReleaseRequestHash(command);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var legalHold = await dbContext.LegalHolds
                .SingleOrDefaultAsync(
                    hold => hold.Id == command.LegalHoldId && hold.EvidenceId == command.EvidenceId,
                    cancellationToken);
            var evidence = await dbContext.EvidenceRecords
                .SingleOrDefaultAsync(item => item.Id == command.EvidenceId, cancellationToken);
            if (legalHold is null || evidence is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            var authority = await RequireProductPermissionAsync(
                actor,
                ProductAuthorityPermissions.RequestLegalHoldRelease,
                ProductAuthorityResourceTypes.GovernanceLegalHold,
                command.LegalHoldId.ToString("D"),
                ProductAuthorityCapabilities.GovernanceLegalHold,
                ProductAuthorityActions.LegalHoldReleaseRequest,
                ProductAuthorityRoleNames.LegalHoldAdministrator,
                cancellationToken);

            var existingByIdempotency = await dbContext.LegalHoldReleaseRequests
                .SingleOrDefaultAsync(request => request.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByIdempotency is not null)
            {
                if (!existingByIdempotency.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different Legal Hold release request.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToReleaseRequestResult(existingByIdempotency, IdempotentReplay: true);
            }

            if (legalHold.ReleasedAt.HasValue)
            {
                throw new RetentionGovernanceConflictException("Legal Hold has already been released.");
            }

            var pendingRequestExists = await dbContext.LegalHoldReleaseRequests.AnyAsync(
                request => request.LegalHoldId == command.LegalHoldId && request.State == LegalHoldReleaseStates.Pending,
                cancellationToken);
            if (pendingRequestExists)
            {
                throw new RetentionGovernanceConflictException("A pending Legal Hold release request already exists.");
            }

            var releaseRequest = new LegalHoldReleaseRequest
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                LegalHoldId = legalHold.Id,
                EvidenceId = legalHold.EvidenceId,
                RequestedBy = actor,
                RequestReason = command.Reason.Trim(),
                RequestedAt = now,
                RequestAuthorityVersion = authority.AuthorityVersion,
                RequestPolicyIdentifier = authority.PolicyIdentifier,
                RequestPolicyVersion = authority.PolicyVersion,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };
            dbContext.LegalHoldReleaseRequests.Add(releaseRequest);

            AddAudit(
                context,
                actor,
                "Retention.LegalHoldReleaseRequested",
                "LegalHold",
                legalHold.Id.ToString("D"),
                $"legal-hold-release-request:{releaseRequest.Id}",
                "Succeeded",
                now,
                new
                {
                    releaseRequestId = releaseRequest.Id,
                    legalHoldId = legalHold.Id,
                    evidenceId = legalHold.EvidenceId.ToString(),
                    authority.AuthorityVersion,
                    reasonHash = Hash(command.Reason.Trim()),
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });

            await AddOutboxAsync(
                context,
                "LegalHoldReleaseRequested",
                now,
                new
                {
                    eventVersion = 1,
                    releaseRequestId = releaseRequest.Id.ToString("D"),
                    legalHoldId = legalHold.Id.ToString("D"),
                    evidenceId = legalHold.EvidenceId.ToString(),
                    aggregateId = legalHold.EvidenceId.ToString(),
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToReleaseRequestResult(releaseRequest, IdempotentReplay: false);
        });
    }

    public async Task<LegalHoldReleaseApprovalResult> ApproveLegalHoldReleaseAsync(
        LegalHoldReleaseApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ValidateReleaseApproval(command);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for retention governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeReleaseApprovalIdempotencyKey(command);
        var requestHash = ComputeReleaseApprovalRequestHash(command);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var existingByApprovalIdempotency = await dbContext.LegalHoldReleaseRequests
                .SingleOrDefaultAsync(request => request.ApprovalIdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByApprovalIdempotency is not null)
            {
                if (!string.Equals(existingByApprovalIdempotency.ApprovalRequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different Legal Hold release approval.");
                }

                var replayHold = await dbContext.LegalHolds
                    .SingleAsync(hold => hold.Id == existingByApprovalIdempotency.LegalHoldId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToReleaseApprovalResult(
                    existingByApprovalIdempotency,
                    replayHold,
                    IdempotentReplay: true);
            }

            var releaseRequest = await dbContext.LegalHoldReleaseRequests
                .SingleOrDefaultAsync(request => request.Id == command.ReleaseRequestId, cancellationToken);
            if (releaseRequest is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            var legalHold = await dbContext.LegalHolds
                .SingleOrDefaultAsync(hold => hold.Id == releaseRequest.LegalHoldId, cancellationToken);
            var evidence = await dbContext.EvidenceRecords
                .SingleOrDefaultAsync(item => item.Id == releaseRequest.EvidenceId, cancellationToken);
            if (legalHold is null || evidence is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            if (releaseRequest.State == LegalHoldReleaseStates.Approved || legalHold.ReleasedAt.HasValue)
            {
                throw new RetentionGovernanceConflictException("Legal Hold release request has already been approved.");
            }

            var authority = await RequireProductPermissionAsync(
                actor,
                ProductAuthorityPermissions.ApproveLegalHoldRelease,
                ProductAuthorityResourceTypes.GovernanceLegalHold,
                releaseRequest.LegalHoldId.ToString("D"),
                ProductAuthorityCapabilities.GovernanceLegalHold,
                ProductAuthorityActions.LegalHoldReleaseApprove,
                ProductAuthorityRoleNames.LegalHoldAdministrator,
                cancellationToken);

            var separationOfDuty = await productAuthorityService.EvaluateSeparationOfDutyAsync(
                new ProductSeparationOfDutyRequest(
                    actor,
                    releaseRequest.RequestedBy,
                    "LegalHoldReleaseApproval"),
                cancellationToken);
            if (!separationOfDuty.Succeeded)
            {
                AddAudit(
                    context,
                    actor,
                    "Retention.LegalHoldReleaseDenied",
                    "LegalHoldReleaseRequest",
                    releaseRequest.Id.ToString("D"),
                    $"legal-hold-release-denied:{releaseRequest.Id}",
                    "Denied",
                    now,
                    new
                    {
                        releaseRequestId = releaseRequest.Id,
                        releaseRequest.LegalHoldId,
                        evidenceId = releaseRequest.EvidenceId.ToString(),
                        denialReasonCode = separationOfDuty.DenialReasonCode,
                        separationOfDuty.PolicyIdentifier,
                        separationOfDuty.PolicyVersion
                    });
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new RetentionGovernanceForbiddenException();
            }

            releaseRequest.State = LegalHoldReleaseStates.Approved;
            releaseRequest.ApprovedBy = actor;
            releaseRequest.ApprovalReason = command.Reason.Trim();
            releaseRequest.ApprovedAt = now;
            releaseRequest.ApprovalAuthorityVersion = authority.AuthorityVersion;
            releaseRequest.ApprovalPolicyIdentifier = authority.PolicyIdentifier;
            releaseRequest.ApprovalPolicyVersion = authority.PolicyVersion;
            releaseRequest.ApprovalIdempotencyKey = idempotencyKey;
            releaseRequest.ApprovalRequestHash = requestHash;
            releaseRequest.ConcurrencyToken = Guid.NewGuid();

            legalHold.ReleasedAt = now;
            legalHold.ReleasedBy = actor;
            legalHold.ReleaseReason = command.Reason.Trim();
            legalHold.ConcurrencyToken = Guid.NewGuid();
            evidence.IsUnderLegalHold = await dbContext.LegalHolds.AnyAsync(
                hold => hold.EvidenceId == evidence.Id
                    && hold.Id != legalHold.Id
                    && !hold.ReleasedAt.HasValue,
                cancellationToken);
            evidence.ConcurrencyToken = Guid.NewGuid();

            var causationId = $"legal-hold-release-approved:{releaseRequest.Id}";
            AddAudit(
                context,
                actor,
                "Retention.LegalHoldReleaseApproved",
                "LegalHoldReleaseRequest",
                releaseRequest.Id.ToString("D"),
                causationId,
                "Succeeded",
                now,
                new
                {
                    releaseRequestId = releaseRequest.Id,
                    legalHoldId = legalHold.Id,
                    evidenceId = evidence.Id.ToString(),
                    requestActor = releaseRequest.RequestedBy,
                    approvalActor = actor,
                    authority.AuthorityVersion,
                    reasonHash = Hash(command.Reason.Trim()),
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });
            await AddLineageAsync(
                context,
                actor,
                evidence.LineageId,
                "LegalHoldReleased",
                causationId,
                now,
                cancellationToken);

            await AddOutboxAsync(
                context,
                "LegalHoldReleased",
                now,
                new
                {
                    eventVersion = 1,
                    releaseRequestId = releaseRequest.Id.ToString("D"),
                    legalHoldId = legalHold.Id.ToString("D"),
                    evidenceId = evidence.Id.ToString(),
                    lineageId = evidence.LineageId.ToString(),
                    aggregateId = evidence.Id.ToString(),
                    evidenceUnderLegalHold = evidence.IsUnderLegalHold,
                    evidencePhysicallyDeleted = false,
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToReleaseApprovalResult(releaseRequest, legalHold, IdempotentReplay: false);
        });
    }

    public async Task<DeletionEligibilityResult> EvaluateDeletionEligibilityAsync(
        DeletionEligibilityCommand command,
        CancellationToken cancellationToken)
    {
        ValidateDeletionEligibility(command);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for retention governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeDeletionEligibilityIdempotencyKey(command);
        var requestHash = ComputeDeletionEligibilityRequestHash(command);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var evidence = await dbContext.EvidenceRecords
                .SingleOrDefaultAsync(item => item.Id == command.EvidenceId, cancellationToken);
            if (evidence is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            var authority = await RequireProductPermissionAsync(
                actor,
                ProductAuthorityPermissions.EvaluateDeletionEligibility,
                ProductAuthorityResourceTypes.GovernanceRetention,
                command.EvidenceId.ToString(),
                ProductAuthorityCapabilities.GovernanceRetention,
                ProductAuthorityActions.DeletionEligibilityEvaluate,
                ProductAuthorityRoleNames.RetentionAdministrator,
                cancellationToken);

            var existingByIdempotency = await dbContext.DeletionEligibilityEvaluations
                .SingleOrDefaultAsync(evaluation => evaluation.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByIdempotency is not null)
            {
                if (!existingByIdempotency.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different deletion eligibility evaluation.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToDeletionEligibilityResult(existingByIdempotency, IdempotentReplay: true);
            }

            var policy = await dbContext.RetentionPolicies
                .SingleOrDefaultAsync(item => item.PolicyKey == evidence.RetentionPolicyKey, cancellationToken);
            var activeLegalHold = await dbContext.LegalHolds.AnyAsync(
                hold => hold.EvidenceId == evidence.Id && !hold.ReleasedAt.HasValue,
                cancellationToken);
            DateTimeOffset? retentionExpiresAt = policy is null
                ? null
                : evidence.CapturedAt.AddDays(policy.RetainForDays);
            var decision = DeletionEligibilityPolicy.Evaluate(new DeletionEligibilityPolicyInput(
                policy is not null,
                retentionExpiresAt,
                activeLegalHold,
                evidence.LifecycleState,
                now));
            var evaluation = new DeletionEligibilityEvaluation
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                EvidenceId = evidence.Id,
                RetentionPolicyId = policy?.Id,
                RetentionPolicyKey = evidence.RetentionPolicyKey,
                RetentionCommencedAt = evidence.CapturedAt,
                RetentionExpiresAt = retentionExpiresAt,
                HasActiveLegalHold = activeLegalHold,
                LifecycleState = evidence.LifecycleState,
                IsEligible = decision.IsEligible,
                ReasonCode = decision.ReasonCode,
                Reason = decision.Reason,
                EvaluatedBy = actor,
                EvaluatedAt = now,
                AuthorityVersion = authority.AuthorityVersion,
                PolicyIdentifier = authority.PolicyIdentifier,
                PolicyVersion = authority.PolicyVersion,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };
            dbContext.DeletionEligibilityEvaluations.Add(evaluation);

            var causationId = $"deletion-eligibility:{evaluation.Id}";
            AddAudit(
                context,
                actor,
                decision.IsEligible
                    ? "Retention.DeletionEligibilityDetermined"
                    : "Retention.DeletionEligibilityDenied",
                "Evidence",
                evidence.Id.ToString(),
                causationId,
                decision.IsEligible ? "Succeeded" : "Denied",
                now,
                new
                {
                    evaluationId = evaluation.Id,
                    evidenceId = evidence.Id.ToString(),
                    evaluation.RetentionPolicyKey,
                    retentionExpiresAt,
                    activeLegalHold,
                    decision.IsEligible,
                    decision.ReasonCode,
                    authority.AuthorityVersion,
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });
            await AddLineageAsync(
                context,
                actor,
                evidence.LineageId,
                decision.IsEligible ? "DeletionEligibilityDetermined" : "DeletionEligibilityDenied",
                causationId,
                now,
                cancellationToken);

            await AddOutboxAsync(
                context,
                "DeletionEligibilityEvaluated",
                now,
                new
                {
                    eventVersion = 1,
                    evaluationId = evaluation.Id.ToString("D"),
                    evidenceId = evidence.Id.ToString(),
                    lineageId = evidence.LineageId.ToString(),
                    aggregateId = evidence.Id.ToString(),
                    isEligible = decision.IsEligible,
                    reasonCode = decision.ReasonCode,
                    evidencePhysicallyDeleted = false,
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToDeletionEligibilityResult(evaluation, IdempotentReplay: false);
        });
    }

    private async Task<ProductAuthorityEvaluationResult> RequireProductPermissionAsync(
        string actor,
        string permissionKey,
        string resourceType,
        string resourceId,
        string capability,
        string action,
        string roleName,
        CancellationToken cancellationToken)
    {
        var authority = await productAuthorityService.EvaluatePermissionAsync(
            new ProductAuthorityEvaluationRequest(
                actor,
                permissionKey,
                resourceType,
                resourceId,
                ProductActorTypes.Human,
                capability,
                action,
                roleName),
            cancellationToken);

        if (!authority.Succeeded)
        {
            throw new RetentionGovernanceForbiddenException();
        }

        return authority;
    }

    private async Task<int> GetNextLineageVersionAsync(LineageId lineageId, CancellationToken cancellationToken)
    {
        var currentVersion = await dbContext.LineageRelationships
            .Where(relationship => relationship.SourceLineageId == lineageId)
            .Select(relationship => (int?)relationship.Version)
            .MaxAsync(cancellationToken);

        return (currentVersion ?? 0) + 1;
    }

    private async Task AddLineageAsync(
        RequestContext context,
        string actor,
        LineageId lineageId,
        string relationshipType,
        string causationId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        dbContext.LineageRelationships.Add(new LineageRelationship
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            SourceLineageId = lineageId,
            TargetLineageId = lineageId,
            RelationshipType = relationshipType,
            ActorOrProcess = actor,
            CorrelationId = context.CorrelationId,
            CausationId = causationId,
            Version = await GetNextLineageVersionAsync(lineageId, cancellationToken),
            ValidFrom = occurredAt
        });
    }

    private void AddAudit(
        RequestContext context,
        string actor,
        string action,
        string targetType,
        string targetId,
        string causationId,
        string outcome,
        DateTimeOffset occurredAt,
        object metadata)
    {
        dbContext.AuditEntries.Add(new AuditEntry
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            ActorSubject = actor,
            AuthorityContext = "Retention",
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            CorrelationId = context.CorrelationId,
            CausationId = causationId,
            Outcome = outcome,
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = occurredAt
        });
    }

    private async Task AddOutboxAsync(
        RequestContext context,
        string messageType,
        DateTimeOffset occurredAt,
        object payload,
        CancellationToken cancellationToken)
    {
        await outboxWriter.AddAsync(new OutboxMessage
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            OwningModule = "Retention",
            MessageType = messageType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CorrelationId = context.CorrelationId,
            OccurredAt = occurredAt,
            AvailableAt = occurredAt
        }, cancellationToken);
    }

    private static string RequireActor(RequestContext context)
    {
        var actor = context.PrincipalSubject.ToString();
        if (string.IsNullOrWhiteSpace(actor) || actor.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("A valid actor context is required for retention governance.");
        }

        return actor;
    }

    private static RetentionPolicyResult ToResult(RetentionPolicy policy, bool IdempotentReplay)
    {
        return new RetentionPolicyResult(
            policy.Id,
            policy.PolicyKey,
            policy.RetainForDays,
            policy.LegalHoldOverridesDeletion,
            policy.CreatedAt,
            IdempotentReplay);
    }

    private static LegalHoldReleaseRequestResult ToReleaseRequestResult(
        LegalHoldReleaseRequest request,
        bool IdempotentReplay)
    {
        return new LegalHoldReleaseRequestResult(
            request.Id,
            request.LegalHoldId,
            request.EvidenceId,
            request.State,
            request.RequestedAt,
            IdempotentReplay);
    }

    private static LegalHoldReleaseApprovalResult ToReleaseApprovalResult(
        LegalHoldReleaseRequest request,
        LegalHold legalHold,
        bool IdempotentReplay)
    {
        return new LegalHoldReleaseApprovalResult(
            request.Id,
            request.LegalHoldId,
            request.EvidenceId,
            request.State,
            legalHold.ReleasedAt ?? request.ApprovedAt ?? DateTimeOffset.MinValue,
            EvidenceUnderLegalHold: false,
            EvidencePhysicallyDeleted: false,
            IdempotentReplay);
    }

    private static DeletionEligibilityResult ToDeletionEligibilityResult(
        DeletionEligibilityEvaluation evaluation,
        bool IdempotentReplay)
    {
        return new DeletionEligibilityResult(
            evaluation.Id,
            evaluation.EvidenceId,
            evaluation.IsEligible,
            evaluation.ReasonCode,
            evaluation.Reason,
            evaluation.RetentionCommencedAt,
            evaluation.RetentionExpiresAt,
            evaluation.HasActiveLegalHold,
            evaluation.LifecycleState,
            EvidencePhysicallyDeleted: false,
            IdempotentReplay);
    }

    private static void ValidatePolicy(RetentionPolicyCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(command.PolicyKey), !PolicyKeyRegex.IsMatch(command.PolicyKey ?? string.Empty), "Policy key must be 3-128 characters using letters, numbers, dot, underscore, colon or dash.");
        AddIf(errors, nameof(command.Description), command.Description is null || command.Description.Length > 512, "Description must not exceed 512 characters.");
        AddIf(errors, nameof(command.RetainForDays), command.RetainForDays < 1 || command.RetainForDays > 36500, "Retention duration must be between 1 and 36500 days.");
        ValidateIdempotencyKey(errors, command.IdempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static void ValidateLegalHold(PlaceLegalHoldCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(command.EvidenceId), command.EvidenceId.Value == Guid.Empty, "Evidence id is required.");
        AddIf(errors, nameof(command.Reason), string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 512, "Reason is required and must not exceed 512 characters.");
        ValidateIdempotencyKey(errors, command.IdempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static void ValidateReleaseRequest(LegalHoldReleaseRequestCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(command.EvidenceId), command.EvidenceId.Value == Guid.Empty, "Evidence id is required.");
        AddIf(errors, nameof(command.LegalHoldId), command.LegalHoldId == Guid.Empty, "Legal Hold id is required.");
        AddIf(errors, nameof(command.Reason), string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 512, "Reason is required and must not exceed 512 characters.");
        ValidateIdempotencyKey(errors, command.IdempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static void ValidateReleaseApproval(LegalHoldReleaseApprovalCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(command.ReleaseRequestId), command.ReleaseRequestId == Guid.Empty, "Legal Hold release request id is required.");
        AddIf(errors, nameof(command.Reason), string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 512, "Reason is required and must not exceed 512 characters.");
        ValidateIdempotencyKey(errors, command.IdempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static void ValidateDeletionEligibility(DeletionEligibilityCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(command.EvidenceId), command.EvidenceId.Value == Guid.Empty, "Evidence id is required.");
        ValidateIdempotencyKey(errors, command.IdempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static void ValidateIdempotencyKey(Dictionary<string, string[]> errors, string? idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            AddIf(errors, "IdempotencyKey", !IdempotencyRegex.IsMatch(idempotencyKey), "Idempotency key must be 8-128 characters using letters, numbers, dot, underscore, colon or dash.");
        }
    }

    private static void AddIf(Dictionary<string, string[]> errors, string field, bool condition, string message)
    {
        if (condition)
        {
            errors[field] = [message];
        }
    }

    private static string NormalizePolicyIdempotencyKey(RetentionPolicyCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputePolicyRequestHash(command)}";
    }

    private static string NormalizeLegalHoldIdempotencyKey(PlaceLegalHoldCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeLegalHoldRequestHash(command)}";
    }

    private static string NormalizeReleaseRequestIdempotencyKey(LegalHoldReleaseRequestCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeReleaseRequestHash(command)}";
    }

    private static string NormalizeReleaseApprovalIdempotencyKey(LegalHoldReleaseApprovalCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeReleaseApprovalRequestHash(command)}";
    }

    private static string NormalizeDeletionEligibilityIdempotencyKey(DeletionEligibilityCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"generated:{Guid.NewGuid():N}";
    }

    private static string ComputePolicyRequestHash(RetentionPolicyCommand command)
    {
        return Hash(string.Join(
            '|',
            command.PolicyKey.Trim().ToUpperInvariant(),
            command.Description.Trim(),
            command.RetainForDays,
            command.LegalHoldOverridesDeletion));
    }

    private static string ComputeLegalHoldRequestHash(PlaceLegalHoldCommand command)
    {
        return Hash(string.Join(
            '|',
            command.EvidenceId.ToString(),
            command.Reason.Trim()));
    }

    private static string ComputeReleaseRequestHash(LegalHoldReleaseRequestCommand command)
    {
        return Hash(string.Join(
            '|',
            command.EvidenceId.ToString(),
            command.LegalHoldId.ToString("D"),
            command.Reason.Trim()));
    }

    private static string ComputeReleaseApprovalRequestHash(LegalHoldReleaseApprovalCommand command)
    {
        return Hash(string.Join(
            '|',
            command.ReleaseRequestId.ToString("D"),
            command.Reason.Trim()));
    }

    private static string ComputeDeletionEligibilityRequestHash(DeletionEligibilityCommand command)
    {
        return Hash(command.EvidenceId.ToString());
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}