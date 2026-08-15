using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

using LegalHold = DataLooMStudio.Modules.Retention.LegalHold;
using RetentionPolicy = DataLooMStudio.Modules.Retention.RetentionPolicy;

namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed class RetentionGovernanceService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    IClock clock,
    IProductAuthorityService productAuthorityService,
    IOutboxWriter outboxWriter,
    PostgresRlsSessionContext rlsSessionContext) : IRetentionGovernanceService
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
                now,
                new
                {
                    policy.PolicyKey,
                    policy.RetainForDays,
                    policy.LegalHoldOverridesDeletion,
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });

            await outboxWriter.AddAsync(new OutboxMessage
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                OwningModule = "Retention",
                MessageType = "RetentionPolicyDefined",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventVersion = 1,
                    policyId = policy.Id.ToString("D"),
                    policyKey = policy.PolicyKey,
                    aggregateId = policy.PolicyKey,
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                }),
                CorrelationId = context.CorrelationId,
                OccurredAt = now,
                AvailableAt = now
            }, cancellationToken);

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
            dbContext.LegalHolds.Add(legalHold);

            var causationId = $"legal-hold:{legalHold.Id}";
            AddAudit(
                context,
                actor,
                "Retention.LegalHoldPlaced",
                "Evidence",
                command.EvidenceId.ToString(),
                causationId,
                now,
                new
                {
                    legalHoldId = legalHold.Id,
                    reasonHash = Hash(command.Reason.Trim()),
                    IdempotencyKeyHash = Hash(idempotencyKey)
                });
            dbContext.LineageRelationships.Add(new LineageRelationship
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                SourceLineageId = evidence.LineageId,
                TargetLineageId = evidence.LineageId,
                RelationshipType = "LegalHoldPlaced",
                ActorOrProcess = actor,
                CorrelationId = context.CorrelationId,
                CausationId = causationId,
                Version = await GetNextLineageVersionAsync(evidence.LineageId, cancellationToken),
                ValidFrom = now
            });

            await outboxWriter.AddAsync(new OutboxMessage
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                OwningModule = "Retention",
                MessageType = "LegalHoldPlaced",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventVersion = 1,
                    legalHoldId = legalHold.Id.ToString("D"),
                    evidenceId = command.EvidenceId.ToString(),
                    lineageId = evidence.LineageId.ToString(),
                    aggregateId = command.EvidenceId.ToString(),
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString()
                }),
                CorrelationId = context.CorrelationId,
                OccurredAt = now,
                AvailableAt = now
            }, cancellationToken);

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

    private async Task RequireProductPermissionAsync(
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
    }

    private async Task<int> GetNextLineageVersionAsync(LineageId lineageId, CancellationToken cancellationToken)
    {
        var currentVersion = await dbContext.LineageRelationships
            .Where(relationship => relationship.SourceLineageId == lineageId)
            .Select(relationship => (int?)relationship.Version)
            .MaxAsync(cancellationToken);

        return (currentVersion ?? 0) + 1;
    }

    private void AddAudit(
        RequestContext context,
        string actor,
        string action,
        string targetType,
        string targetId,
        string causationId,
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
            Outcome = "Succeeded",
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = occurredAt
        });
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

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}