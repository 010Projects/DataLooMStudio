using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

using DeletionEligibilityEvaluation = DataLooMStudio.Modules.Retention.DeletionEligibilityEvaluation;
using DisposalPolicy = DataLooMStudio.Modules.Retention.DisposalPolicy;
using DisposalPolicyDecision = DataLooMStudio.Modules.Retention.DisposalPolicyDecision;
using DisposalPolicyInput = DataLooMStudio.Modules.Retention.DisposalPolicyInput;
using DisposalRecord = DataLooMStudio.Modules.Retention.DisposalRecord;
using DisposalRecordStates = DataLooMStudio.Modules.Retention.DisposalRecordStates;
using EvidenceRecord = DataLooMStudio.Modules.Evidence.EvidenceRecord;
using EvidenceVersion = DataLooMStudio.Modules.Evidence.EvidenceVersion;

namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed partial class RetentionGovernanceService
{
    private static readonly TimeSpan DisposalCommandMaximumAge = TimeSpan.FromMinutes(15);

    public async Task<EvidenceDisposalResult> RequestEvidenceDisposalAsync(
        EvidenceDisposalRequestCommand command,
        CancellationToken cancellationToken)
    {
        ValidateDisposalRequest(command);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Evidence disposal governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeDisposalRequestIdempotencyKey(command);
        var requestHash = ComputeDisposalRequestHash(command, actor);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var evidence = await dbContext.EvidenceRecords
                .SingleOrDefaultAsync(item => item.Id == command.EvidenceId, cancellationToken);
            var eligibility = await dbContext.DeletionEligibilityEvaluations
                .SingleOrDefaultAsync(item =>
                    item.Id == command.DeletionEligibilityEvaluationId
                    && item.EvidenceId == command.EvidenceId,
                    cancellationToken);
            if (evidence is null || eligibility is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            var authority = await RequireDisposalPermissionAsync(
                actor,
                ProductAuthorityPermissions.RequestEvidenceDisposal,
                ProductAuthorityResourceTypes.EvidenceDisposal,
                command.EvidenceId.ToString(),
                ProductAuthorityCapabilities.EvidenceDisposal,
                ProductAuthorityActions.EvidenceDisposalRequest,
                ProductAuthorityRoleNames.RetentionAdministrator,
                ProductActorTypes.Human,
                cancellationToken);

            var existingByIdempotency = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(record => record.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByIdempotency is not null)
            {
                if (!existingByIdempotency.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different Evidence disposal request.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(existingByIdempotency, IdempotentReplay: true);
            }

            var policyDecision = await EvaluateCurrentDisposalPolicyAsync(
                evidence,
                eligibility,
                now,
                cancellationToken);
            if (!policyDecision.IsPermitted)
            {
                AddAudit(
                    context,
                    actor,
                    "Evidence.DisposalRequestDenied",
                    "Evidence",
                    evidence.Id.ToString(),
                    $"evidence-disposal-request-denied:{command.DeletionEligibilityEvaluationId}",
                    "Denied",
                    now,
                    new
                    {
                        evidenceId = evidence.Id.ToString(),
                        eligibilityId = eligibility.Id,
                        policyDecision.ReasonCode,
                        policyDecision.Reason,
                        authority.AuthorityVersion,
                        IdempotencyKeyHash = Hash(idempotencyKey)
                    });
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new RetentionGovernanceForbiddenException();
            }

            var currentVersion = await RequireCurrentEvidenceVersionAsync(evidence, cancellationToken);
            var record = new DisposalRecord
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                EvidenceId = evidence.Id,
                DeletionEligibilityEvaluationId = eligibility.Id,
                RetentionPolicyKey = eligibility.RetentionPolicyKey,
                RetentionExpiresAt = eligibility.RetentionExpiresAt,
                LifecycleState = evidence.LifecycleState,
                StorageObjectReference = currentVersion.StorageObjectReference,
                ExpectedSha256Hash = currentVersion.ContentHash,
                RequestedBy = actor,
                RequestReason = command.Reason.Trim(),
                RequestedAt = now,
                RequestAuthorityVersion = authority.AuthorityVersion,
                RequestPolicyIdentifier = authority.PolicyIdentifier,
                RequestPolicyVersion = authority.PolicyVersion,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };
            dbContext.DisposalRecords.Add(record);

            var causationId = $"evidence-disposal:{record.Id}";
            AddAudit(
                context,
                actor,
                "Evidence.DisposalRequested",
                "Evidence",
                evidence.Id.ToString(),
                causationId,
                "Succeeded",
                now,
                new
                {
                    disposalRecordId = record.Id,
                    eligibilityId = eligibility.Id,
                    evidenceId = evidence.Id.ToString(),
                    authority.AuthorityVersion,
                    reasonHash = Hash(command.Reason.Trim()),
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });
            await AddLineageAsync(
                context,
                actor,
                evidence.LineageId,
                "EvidenceDisposalRequested",
                causationId,
                now,
                cancellationToken);
            await AddOutboxAsync(
                context,
                "EvidenceDisposalRequested",
                now,
                new
                {
                    eventVersion = 1,
                    disposalRecordId = record.Id.ToString("D"),
                    evidenceId = evidence.Id.ToString(),
                    eligibilityId = eligibility.Id.ToString("D"),
                    aggregateId = evidence.Id.ToString(),
                    evidencePhysicallyDeleted = false,
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDisposalResult(record, IdempotentReplay: false);
        });
    }

    public async Task<EvidenceDisposalResult> ApproveEvidenceDisposalAsync(
        EvidenceDisposalApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ValidateDisposalApproval(command);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Evidence disposal governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeDisposalApprovalIdempotencyKey(command);
        var requestHash = ComputeDisposalApprovalRequestHash(command, actor);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var existingByApprovalIdempotency = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(record => record.ApprovalIdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByApprovalIdempotency is not null)
            {
                if (!string.Equals(existingByApprovalIdempotency.ApprovalRequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different Evidence disposal approval.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(existingByApprovalIdempotency, IdempotentReplay: true);
            }

            var record = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(item => item.Id == command.DisposalRecordId, cancellationToken);
            if (record is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            if (!record.State.Equals(DisposalRecordStates.Requested, StringComparison.Ordinal))
            {
                throw new RetentionGovernanceConflictException("Evidence disposal request is not pending approval.");
            }

            var evidence = await RequireEvidenceAsync(record.EvidenceId, cancellationToken);
            var eligibility = await RequireEligibilityAsync(record, cancellationToken);
            var authority = await RequireDisposalPermissionAsync(
                actor,
                ProductAuthorityPermissions.ApproveEvidenceDisposal,
                ProductAuthorityResourceTypes.EvidenceDisposal,
                record.Id.ToString("D"),
                ProductAuthorityCapabilities.EvidenceDisposal,
                ProductAuthorityActions.EvidenceDisposalApprove,
                ProductAuthorityRoleNames.RetentionAdministrator,
                ProductActorTypes.Human,
                cancellationToken);
            var separationOfDuty = await productAuthorityService.EvaluateSeparationOfDutyAsync(
                new ProductSeparationOfDutyRequest(
                    actor,
                    record.RequestedBy,
                    "EvidenceDisposalApproval"),
                cancellationToken);
            if (!separationOfDuty.Succeeded)
            {
                AddAudit(
                    context,
                    actor,
                    "Evidence.DisposalApprovalDenied",
                    "EvidenceDisposal",
                    record.Id.ToString("D"),
                    $"evidence-disposal-approval-denied:{record.Id}",
                    "Denied",
                    now,
                    new
                    {
                        disposalRecordId = record.Id,
                        evidenceId = record.EvidenceId.ToString(),
                        denialReasonCode = separationOfDuty.DenialReasonCode,
                        separationOfDuty.PolicyIdentifier,
                        separationOfDuty.PolicyVersion
                    });
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new RetentionGovernanceForbiddenException();
            }

            var policyDecision = await EvaluateCurrentDisposalPolicyAsync(evidence, eligibility, now, cancellationToken);
            if (!policyDecision.IsPermitted)
            {
                AddAudit(
                    context,
                    actor,
                    "Evidence.DisposalApprovalDenied",
                    "EvidenceDisposal",
                    record.Id.ToString("D"),
                    $"evidence-disposal-approval-denied:{record.Id}",
                    "Denied",
                    now,
                    new
                    {
                        disposalRecordId = record.Id,
                        evidenceId = record.EvidenceId.ToString(),
                        policyDecision.ReasonCode,
                        policyDecision.Reason,
                        authority.AuthorityVersion
                    });
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new RetentionGovernanceForbiddenException();
            }

            record.State = DisposalRecordStates.Approved;
            record.ApprovedBy = actor;
            record.ApprovalReason = command.Reason.Trim();
            record.ApprovedAt = now;
            record.ApprovalAuthorityVersion = authority.AuthorityVersion;
            record.ApprovalPolicyIdentifier = authority.PolicyIdentifier;
            record.ApprovalPolicyVersion = authority.PolicyVersion;
            record.ApprovalIdempotencyKey = idempotencyKey;
            record.ApprovalRequestHash = requestHash;
            record.ConcurrencyToken = Guid.NewGuid();

            var causationId = $"evidence-disposal-approved:{record.Id}";
            AddAudit(
                context,
                actor,
                "Evidence.DisposalApproved",
                "EvidenceDisposal",
                record.Id.ToString("D"),
                causationId,
                "Succeeded",
                now,
                new
                {
                    disposalRecordId = record.Id,
                    evidenceId = record.EvidenceId.ToString(),
                    authority.AuthorityVersion,
                    reasonHash = Hash(command.Reason.Trim()),
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });
            await AddLineageAsync(
                context,
                actor,
                evidence.LineageId,
                "EvidenceDisposalApproved",
                causationId,
                now,
                cancellationToken);
            await AddOutboxAsync(
                context,
                "EvidenceDisposalApproved",
                now,
                new
                {
                    eventVersion = 1,
                    disposalRecordId = record.Id.ToString("D"),
                    evidenceId = record.EvidenceId.ToString(),
                    aggregateId = record.EvidenceId.ToString(),
                    evidencePhysicallyDeleted = false,
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDisposalResult(record, IdempotentReplay: false);
        });
    }

    public async Task<EvidenceDisposalResult> QueueEvidenceDisposalAsync(
        EvidenceDisposalQueueCommand command,
        CancellationToken cancellationToken)
    {
        ValidateDisposalRecordCommand(command.DisposalRecordId, command.IdempotencyKey, nameof(command.DisposalRecordId));

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Evidence disposal governance.");
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeDisposalQueueIdempotencyKey(command);
        var requestHash = ComputeDisposalQueueRequestHash(command, actor);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var existingByQueueIdempotency = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(record => record.QueueIdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByQueueIdempotency is not null)
            {
                if (!string.Equals(existingByQueueIdempotency.QueueRequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different Evidence disposal queue command.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(existingByQueueIdempotency, IdempotentReplay: true);
            }

            var record = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(item => item.Id == command.DisposalRecordId, cancellationToken);
            if (record is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            if (!record.State.Equals(DisposalRecordStates.Approved, StringComparison.Ordinal))
            {
                throw new RetentionGovernanceConflictException("Evidence disposal request is not approved for queueing.");
            }

            var authority = await RequireDisposalPermissionAsync(
                actor,
                ProductAuthorityPermissions.QueueEvidenceDisposal,
                ProductAuthorityResourceTypes.EvidenceDisposal,
                record.Id.ToString("D"),
                ProductAuthorityCapabilities.EvidenceDisposal,
                ProductAuthorityActions.EvidenceDisposalQueue,
                ProductAuthorityRoleNames.RetentionAdministrator,
                ProductActorTypes.Human,
                cancellationToken);
            if (!record.ApprovedAt.HasValue || now - record.ApprovedAt.Value > DisposalCommandMaximumAge)
            {
                await RecordDisposalDeniedAsync(
                    context,
                    actor,
                    "Evidence.DisposalQueueDenied",
                    record,
                    "AuthorityStale",
                    "Disposal approval is outside the allowed command window.",
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new RetentionGovernanceForbiddenException();
            }

            var evidence = await RequireEvidenceAsync(record.EvidenceId, cancellationToken);
            var eligibility = await RequireEligibilityAsync(record, cancellationToken);
            var policyDecision = await EvaluateCurrentDisposalPolicyAsync(evidence, eligibility, now, cancellationToken);
            if (!policyDecision.IsPermitted)
            {
                await RecordDisposalDeniedAsync(
                    context,
                    actor,
                    "Evidence.DisposalQueueDenied",
                    record,
                    policyDecision.ReasonCode,
                    policyDecision.Reason,
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new RetentionGovernanceForbiddenException();
            }

            record.State = DisposalRecordStates.Queued;
            record.QueuedBy = actor;
            record.QueuedAt = now;
            record.QueueIdempotencyKey = idempotencyKey;
            record.QueueRequestHash = requestHash;
            record.ConcurrencyToken = Guid.NewGuid();

            var causationId = $"evidence-disposal-queued:{record.Id}";
            AddAudit(
                context,
                actor,
                "Evidence.DisposalQueued",
                "EvidenceDisposal",
                record.Id.ToString("D"),
                causationId,
                "Succeeded",
                now,
                new
                {
                    disposalRecordId = record.Id,
                    evidenceId = record.EvidenceId.ToString(),
                    authority.AuthorityVersion,
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });
            await AddOutboxAsync(
                context,
                "EvidenceDisposalQueued",
                now,
                new
                {
                    eventVersion = 1,
                    disposalRecordId = record.Id.ToString("D"),
                    evidenceId = record.EvidenceId.ToString(),
                    aggregateId = record.EvidenceId.ToString(),
                    evidencePhysicallyDeleted = false,
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDisposalResult(record, IdempotentReplay: false);
        });
    }

    public async Task<EvidenceDisposalResult> ExecuteEvidenceDisposalAsync(
        EvidenceDisposalExecutionCommand command,
        CancellationToken cancellationToken)
    {
        ValidateDisposalRecordCommand(command.DisposalRecordId, command.IdempotencyKey, nameof(command.DisposalRecordId));

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Evidence disposal governance.");
        var workload = context.PrincipalSubject.ToString();
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeDisposalExecutionIdempotencyKey(command);
        var requestHash = ComputeDisposalExecutionRequestHash(command, workload);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var existingByExecutionIdempotency = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(record => record.ExecutionIdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByExecutionIdempotency is not null)
            {
                if (!string.Equals(existingByExecutionIdempotency.ExecutionRequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different Evidence disposal execution command.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(existingByExecutionIdempotency, IdempotentReplay: true);
            }

            var record = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(item => item.Id == command.DisposalRecordId, cancellationToken);
            if (record is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            if (!record.State.Equals(DisposalRecordStates.Queued, StringComparison.Ordinal)
                && !record.State.Equals(DisposalRecordStates.Failed, StringComparison.Ordinal)
                && !record.State.Equals(DisposalRecordStates.Suspended, StringComparison.Ordinal))
            {
                throw new RetentionGovernanceConflictException("Evidence disposal record is not queued for execution.");
            }

            var authority = await RequireDisposalPermissionAsync(
                workload,
                ProductAuthorityPermissions.ExecuteEvidenceDisposal,
                ProductAuthorityResourceTypes.EvidenceDisposal,
                record.Id.ToString("D"),
                ProductAuthorityCapabilities.WorkloadProcessing,
                ProductAuthorityActions.EvidenceDisposalExecute,
                null,
                ProductActorTypes.Workload,
                cancellationToken);
            if (!record.QueuedAt.HasValue || now - record.QueuedAt.Value > DisposalCommandMaximumAge)
            {
                await SuspendDisposalAsync(
                    context,
                    workload,
                    record,
                    "Evidence.DisposalExecutionSuspended",
                    "CommandExpired",
                    "Queued disposal command is outside the allowed execution window.",
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(record, IdempotentReplay: false);
            }

            var evidence = await RequireEvidenceAsync(record.EvidenceId, cancellationToken);
            var eligibility = await RequireEligibilityAsync(record, cancellationToken);
            var policyDecision = await EvaluateCurrentDisposalPolicyAsync(evidence, eligibility, now, cancellationToken);
            if (!policyDecision.IsPermitted)
            {
                await SuspendDisposalAsync(
                    context,
                    workload,
                    record,
                    "Evidence.DisposalExecutionSuspended",
                    policyDecision.ReasonCode,
                    policyDecision.Reason,
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(record, IdempotentReplay: false);
            }

            record.State = DisposalRecordStates.Executing;
            record.ExecutedBy = workload;
            record.ExecutionStartedAt = now;
            record.ExecutionAuthorityVersion = authority.AuthorityVersion;
            record.ExecutionPolicyIdentifier = authority.PolicyIdentifier;
            record.ExecutionPolicyVersion = authority.PolicyVersion;
            record.ExecutionIdempotencyKey = idempotencyKey;
            record.ExecutionRequestHash = requestHash;
            record.AttemptCount += 1;
            record.LastAttemptAt = now;
            record.ConcurrencyToken = Guid.NewGuid();

            AddAudit(
                context,
                workload,
                "Evidence.DisposalExecutionStarted",
                "EvidenceDisposal",
                record.Id.ToString("D"),
                $"evidence-disposal-execution-started:{record.Id}:{record.AttemptCount}",
                "Started",
                now,
                new
                {
                    disposalRecordId = record.Id,
                    evidenceId = record.EvidenceId.ToString(),
                    authority.AuthorityVersion,
                    attemptCount = record.AttemptCount,
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });

            EvidenceDisposalObjectResult storageResult;
            try
            {
                storageResult = await disposalObjectStore.DisposeEvidenceContentAsync(
                    new EvidenceDisposalObjectRequest(
                        context.TenantId,
                        context.WorkspaceId,
                        record.EvidenceId,
                        record.Id,
                        record.StorageObjectReference,
                        record.ExpectedSha256Hash,
                        workload),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                storageResult = new EvidenceDisposalObjectResult(
                    EvidenceDisposalObjectOutcomes.Failed,
                    "StorageException",
                    EvidencePhysicallyDeleted: false,
                    exception.Message);
            }

            ApplyStorageExecutionResult(context, workload, record, storageResult, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDisposalResult(record, IdempotentReplay: false);
        });
    }

    public async Task<EvidenceDisposalResult> ReconcileEvidenceDisposalAsync(
        EvidenceDisposalReconciliationCommand command,
        CancellationToken cancellationToken)
    {
        ValidateDisposalRecordCommand(command.DisposalRecordId, command.IdempotencyKey, nameof(command.DisposalRecordId));

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Evidence disposal governance.");
        var workload = context.PrincipalSubject.ToString();
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeDisposalReconciliationIdempotencyKey(command);
        var requestHash = ComputeDisposalReconciliationRequestHash(command, workload);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var existingByReconciliationIdempotency = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(record => record.ReconciliationIdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByReconciliationIdempotency is not null)
            {
                if (!string.Equals(existingByReconciliationIdempotency.ReconciliationRequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new RetentionGovernanceConflictException("The idempotency key was already used for a different Evidence disposal reconciliation command.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(existingByReconciliationIdempotency, IdempotentReplay: true);
            }

            var record = await dbContext.DisposalRecords
                .SingleOrDefaultAsync(item => item.Id == command.DisposalRecordId, cancellationToken);
            if (record is null)
            {
                throw new RetentionGovernanceForbiddenException();
            }

            if (!record.State.Equals(DisposalRecordStates.StorageDisposed, StringComparison.Ordinal)
                && !record.State.Equals(DisposalRecordStates.Reconciled, StringComparison.Ordinal)
                && !record.State.Equals(DisposalRecordStates.Completed, StringComparison.Ordinal))
            {
                throw new RetentionGovernanceConflictException("Evidence disposal record is not ready for reconciliation.");
            }

            var authority = await RequireDisposalPermissionAsync(
                workload,
                ProductAuthorityPermissions.ReconcileEvidenceDisposal,
                ProductAuthorityResourceTypes.EvidenceDisposal,
                record.Id.ToString("D"),
                ProductAuthorityCapabilities.WorkloadProcessing,
                ProductAuthorityActions.EvidenceDisposalReconcile,
                null,
                ProductActorTypes.Workload,
                cancellationToken);
            var evidence = await RequireEvidenceAsync(record.EvidenceId, cancellationToken);
            var eligibility = await RequireEligibilityAsync(record, cancellationToken);
            var policyDecision = await EvaluateCurrentDisposalPolicyAsync(evidence, eligibility, now, cancellationToken);
            if (!policyDecision.IsPermitted)
            {
                await SuspendDisposalAsync(
                    context,
                    workload,
                    record,
                    "Evidence.DisposalReconciliationSuspended",
                    policyDecision.ReasonCode,
                    policyDecision.Reason,
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToDisposalResult(record, IdempotentReplay: false);
            }

            var reconciliation = await disposalObjectStore.ReconcileEvidenceContentAsync(
                new EvidenceDisposalReconciliationRequest(
                    context.TenantId,
                    context.WorkspaceId,
                    record.EvidenceId,
                    record.Id,
                    record.StorageObjectReference,
                    record.ExpectedSha256Hash),
                cancellationToken);

            record.ReconciledBy = workload;
            record.ReconciledAt = now;
            record.ReconciliationIdempotencyKey = idempotencyKey;
            record.ReconciliationRequestHash = requestHash;
            record.EvidencePhysicallyDeleted = false;
            record.ConcurrencyToken = Guid.NewGuid();

            if (reconciliation.ResurrectionDetected)
            {
                record.State = DisposalRecordStates.Failed;
                record.LastFailureReason = "Reconciliation detected content resurrection.";
                AddAudit(
                    context,
                    workload,
                    "Evidence.DisposalResurrectionDetected",
                    "EvidenceDisposal",
                    record.Id.ToString("D"),
                    $"evidence-disposal-resurrection:{record.Id}",
                    "Failed",
                    now,
                    new
                    {
                        disposalRecordId = record.Id,
                        evidenceId = record.EvidenceId.ToString(),
                        reconciliation.Reason,
                        authority.AuthorityVersion
                    });
            }
            else if (reconciliation.Confirmed)
            {
                record.State = DisposalRecordStates.Completed;
                record.CompletedAt = now;
                record.StorageDisposition = string.IsNullOrWhiteSpace(record.StorageDisposition)
                    ? "SyntheticDisposed"
                    : record.StorageDisposition;
                AddAudit(
                    context,
                    workload,
                    "Evidence.DisposalReconciled",
                    "EvidenceDisposal",
                    record.Id.ToString("D"),
                    $"evidence-disposal-reconciled:{record.Id}",
                    "Succeeded",
                    now,
                    new
                    {
                        disposalRecordId = record.Id,
                        evidenceId = record.EvidenceId.ToString(),
                        reconciliation.Reason,
                        evidencePhysicallyDeleted = false,
                        authority.AuthorityVersion,
                        IdempotencyKeyHash = Hash(idempotencyKey)
                    });
                await AddLineageAsync(
                    context,
                    workload,
                    evidence.LineageId,
                    "EvidenceDisposalReconciled",
                    $"evidence-disposal-reconciled:{record.Id}",
                    now,
                    cancellationToken);
                await AddOutboxAsync(
                    context,
                    "EvidenceDisposalReconciled",
                    now,
                    new
                    {
                        eventVersion = 1,
                        disposalRecordId = record.Id.ToString("D"),
                        evidenceId = record.EvidenceId.ToString(),
                        aggregateId = record.EvidenceId.ToString(),
                        evidencePhysicallyDeleted = false,
                        tenantId = context.TenantId.ToString(),
                        workspaceId = context.WorkspaceId.ToString()
                    },
                    cancellationToken);
            }
            else
            {
                record.State = DisposalRecordStates.Failed;
                record.LastFailureReason = reconciliation.Reason;
                AddAudit(
                    context,
                    workload,
                    "Evidence.DisposalReconciliationFailed",
                    "EvidenceDisposal",
                    record.Id.ToString("D"),
                    $"evidence-disposal-reconciliation-failed:{record.Id}",
                    "Failed",
                    now,
                    new
                    {
                        disposalRecordId = record.Id,
                        evidenceId = record.EvidenceId.ToString(),
                        reconciliation.Reason,
                        authority.AuthorityVersion
                    });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDisposalResult(record, IdempotentReplay: false);
        });
    }

    private async Task<ProductAuthorityEvaluationResult> RequireDisposalPermissionAsync(
        string actor,
        string permissionKey,
        string resourceType,
        string resourceId,
        string capability,
        string action,
        string? roleName,
        string actorType,
        CancellationToken cancellationToken)
    {
        var authority = await productAuthorityService.EvaluatePermissionAsync(
            new ProductAuthorityEvaluationRequest(
                actor,
                permissionKey,
                resourceType,
                resourceId,
                actorType,
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

    private async Task<DisposalPolicyDecision> EvaluateCurrentDisposalPolicyAsync(
        EvidenceRecord evidence,
        DeletionEligibilityEvaluation eligibility,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.RetentionPolicies
            .SingleOrDefaultAsync(item => item.PolicyKey == evidence.RetentionPolicyKey, cancellationToken);
        var activeLegalHold = await dbContext.LegalHolds.AnyAsync(
            hold => hold.EvidenceId == evidence.Id && !hold.ReleasedAt.HasValue,
            cancellationToken);
        DateTimeOffset? currentRetentionExpiresAt = policy is null
            ? null
            : evidence.CapturedAt.AddDays(policy.RetainForDays);

        return DisposalPolicy.Evaluate(new DisposalPolicyInput(
            EligibilityExists: true,
            EligibilityIsApproved: eligibility.IsEligible,
            EligibilityMatchesEvidence: eligibility.EvidenceId == evidence.Id
                && eligibility.RetentionPolicyKey.Equals(evidence.RetentionPolicyKey, StringComparison.Ordinal)
                && eligibility.RetentionCommencedAt == evidence.CapturedAt
                && Nullable.Equals(eligibility.RetentionExpiresAt, currentRetentionExpiresAt)
                && eligibility.LifecycleState.Equals(evidence.LifecycleState, StringComparison.Ordinal),
            RetentionPolicyExists: policy is not null,
            CurrentRetentionExpiresAt: currentRetentionExpiresAt,
            HasActiveLegalHold: activeLegalHold,
            CurrentLifecycleState: evidence.LifecycleState,
            Now: now));
    }

    private async Task<EvidenceRecord> RequireEvidenceAsync(
        EvidenceId evidenceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceRecords
            .SingleOrDefaultAsync(item => item.Id == evidenceId, cancellationToken)
            ?? throw new RetentionGovernanceForbiddenException();
    }

    private async Task<DeletionEligibilityEvaluation> RequireEligibilityAsync(
        DisposalRecord record,
        CancellationToken cancellationToken)
    {
        return await dbContext.DeletionEligibilityEvaluations
            .SingleOrDefaultAsync(item =>
                item.Id == record.DeletionEligibilityEvaluationId
                && item.EvidenceId == record.EvidenceId,
                cancellationToken)
            ?? throw new RetentionGovernanceForbiddenException();
    }

    private async Task<EvidenceVersion> RequireCurrentEvidenceVersionAsync(
        EvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceVersions
            .SingleOrDefaultAsync(version =>
                version.Id == evidence.CurrentVersionId
                && version.EvidenceId == evidence.Id,
                cancellationToken)
            ?? throw new RetentionGovernanceForbiddenException();
    }

    private async Task RecordDisposalDeniedAsync(
        RequestContext context,
        string actor,
        string action,
        DisposalRecord record,
        string reasonCode,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AddAudit(
            context,
            actor,
            action,
            "EvidenceDisposal",
            record.Id.ToString("D"),
            $"{action.ToLowerInvariant()}:{record.Id}",
            "Denied",
            now,
            new
            {
                disposalRecordId = record.Id,
                evidenceId = record.EvidenceId.ToString(),
                reasonCode,
                reason
            });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SuspendDisposalAsync(
        RequestContext context,
        string workload,
        DisposalRecord record,
        string action,
        string reasonCode,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        record.State = DisposalRecordStates.Suspended;
        record.LastFailureReason = $"{reasonCode}: {reason}";
        record.LastAttemptAt = now;
        record.ConcurrencyToken = Guid.NewGuid();
        AddAudit(
            context,
            workload,
            action,
            "EvidenceDisposal",
            record.Id.ToString("D"),
            $"{action.ToLowerInvariant()}:{record.Id}",
            "Suspended",
            now,
            new
            {
                disposalRecordId = record.Id,
                evidenceId = record.EvidenceId.ToString(),
                reasonCode,
                reason,
                evidencePhysicallyDeleted = false
            });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void ApplyStorageExecutionResult(
        RequestContext context,
        string workload,
        DisposalRecord record,
        EvidenceDisposalObjectResult storageResult,
        DateTimeOffset now)
    {
        record.StorageDisposition = storageResult.Disposition;
        record.EvidencePhysicallyDeleted = false;
        record.ConcurrencyToken = Guid.NewGuid();

        if (storageResult.Outcome.Equals(EvidenceDisposalObjectOutcomes.Succeeded, StringComparison.Ordinal))
        {
            record.State = DisposalRecordStates.StorageDisposed;
            record.StorageDisposedAt = now;
            record.LastFailureReason = null;
            AddAudit(
                context,
                workload,
                "Evidence.DisposalStorageDisposed",
                "EvidenceDisposal",
                record.Id.ToString("D"),
                $"evidence-disposal-storage-disposed:{record.Id}",
                "Succeeded",
                now,
                new
                {
                    disposalRecordId = record.Id,
                    evidenceId = record.EvidenceId.ToString(),
                    storageResult.Disposition,
                    evidencePhysicallyDeleted = false
                });
            return;
        }

        if (storageResult.Outcome.Equals(EvidenceDisposalObjectOutcomes.Suspended, StringComparison.Ordinal))
        {
            record.State = DisposalRecordStates.Suspended;
            record.LastFailureReason = storageResult.Reason;
            AddAudit(
                context,
                workload,
                "Evidence.DisposalExecutionSuspended",
                "EvidenceDisposal",
                record.Id.ToString("D"),
                $"evidence-disposal-execution-suspended:{record.Id}:{record.AttemptCount}",
                "Suspended",
                now,
                new
                {
                    disposalRecordId = record.Id,
                    evidenceId = record.EvidenceId.ToString(),
                    storageResult.Disposition,
                    storageResult.Reason,
                    evidencePhysicallyDeleted = false
                });
            return;
        }

        record.State = DisposalRecordStates.Failed;
        record.LastFailureReason = storageResult.Reason;
        AddAudit(
            context,
            workload,
            "Evidence.DisposalExecutionFailed",
            "EvidenceDisposal",
            record.Id.ToString("D"),
            $"evidence-disposal-execution-failed:{record.Id}:{record.AttemptCount}",
            "Failed",
            now,
            new
            {
                disposalRecordId = record.Id,
                evidenceId = record.EvidenceId.ToString(),
                storageResult.Disposition,
                storageResult.Reason,
                evidencePhysicallyDeleted = false
            });
    }

    private static EvidenceDisposalResult ToDisposalResult(
        DisposalRecord record,
        bool IdempotentReplay)
    {
        return new EvidenceDisposalResult(
            record.Id,
            record.EvidenceId,
            record.DeletionEligibilityEvaluationId,
            record.State,
            record.StorageDisposition ?? "NotExecuted",
            EvidencePhysicallyDeleted: false,
            record.AttemptCount,
            record.LastFailureReason,
            IdempotentReplay);
    }

    private static void ValidateDisposalRequest(EvidenceDisposalRequestCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(command.EvidenceId), command.EvidenceId.Value == Guid.Empty, "Evidence id is required.");
        AddIf(errors, nameof(command.DeletionEligibilityEvaluationId), command.DeletionEligibilityEvaluationId == Guid.Empty, "Deletion eligibility evaluation id is required.");
        AddIf(errors, nameof(command.Reason), string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 512, "Reason is required and must not exceed 512 characters.");
        ValidateIdempotencyKey(errors, command.IdempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static void ValidateDisposalApproval(EvidenceDisposalApprovalCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(command.DisposalRecordId), command.DisposalRecordId == Guid.Empty, "Disposal record id is required.");
        AddIf(errors, nameof(command.Reason), string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 512, "Reason is required and must not exceed 512 characters.");
        ValidateIdempotencyKey(errors, command.IdempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static void ValidateDisposalRecordCommand(
        Guid disposalRecordId,
        string? idempotencyKey,
        string fieldName)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, fieldName, disposalRecordId == Guid.Empty, "Disposal record id is required.");
        ValidateIdempotencyKey(errors, idempotencyKey);

        if (errors.Count > 0)
        {
            throw new RetentionGovernanceValidationException(errors);
        }
    }

    private static string NormalizeDisposalRequestIdempotencyKey(EvidenceDisposalRequestCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeDisposalRequestHash(command, string.Empty)}";
    }

    private static string NormalizeDisposalApprovalIdempotencyKey(EvidenceDisposalApprovalCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeDisposalApprovalRequestHash(command, string.Empty)}";
    }

    private static string NormalizeDisposalQueueIdempotencyKey(EvidenceDisposalQueueCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeDisposalQueueRequestHash(command, string.Empty)}";
    }

    private static string NormalizeDisposalExecutionIdempotencyKey(EvidenceDisposalExecutionCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeDisposalExecutionRequestHash(command, string.Empty)}";
    }

    private static string NormalizeDisposalReconciliationIdempotencyKey(EvidenceDisposalReconciliationCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? command.IdempotencyKey.Trim()
            : $"derived:{ComputeDisposalReconciliationRequestHash(command, string.Empty)}";
    }

    private static string ComputeDisposalRequestHash(EvidenceDisposalRequestCommand command, string actor)
    {
        return Hash(string.Join(
            '|',
            actor,
            command.EvidenceId.ToString(),
            command.DeletionEligibilityEvaluationId.ToString("D"),
            command.Reason.Trim()));
    }

    private static string ComputeDisposalApprovalRequestHash(EvidenceDisposalApprovalCommand command, string actor)
    {
        return Hash(string.Join(
            '|',
            actor,
            command.DisposalRecordId.ToString("D"),
            command.Reason.Trim()));
    }

    private static string ComputeDisposalQueueRequestHash(EvidenceDisposalQueueCommand command, string actor)
    {
        return Hash(string.Join('|', actor, command.DisposalRecordId.ToString("D")));
    }

    private static string ComputeDisposalExecutionRequestHash(EvidenceDisposalExecutionCommand command, string actor)
    {
        return Hash(string.Join('|', actor, command.DisposalRecordId.ToString("D")));
    }

    private static string ComputeDisposalReconciliationRequestHash(EvidenceDisposalReconciliationCommand command, string actor)
    {
        return Hash(string.Join('|', actor, command.DisposalRecordId.ToString("D")));
    }
}