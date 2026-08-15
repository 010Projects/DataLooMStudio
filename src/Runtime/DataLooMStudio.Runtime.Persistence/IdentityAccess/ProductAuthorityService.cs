using System.Text.Json;

using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed class ProductAuthorityService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    IClock clock,
    IProductAuthorityAuditStore auditStore) : IProductAuthorityService
{
    public async Task<ProductAuthorityEvaluationResult> EvaluatePermissionAsync(
        ProductAuthorityEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var context = requestContextAccessor.Current;
        var actorSubject = request.ActorSubject.Trim();
        var permissionKey = request.PermissionKey.Trim();
        var resourceType = request.ResourceType.Trim();
        var resourceId = request.ResourceId.Trim();
        var actorType = request.ActorType.Trim();

        if (context is null)
        {
            return ProductAuthorityEvaluationResult.Denied(
                ProductAuthorityDenyReasonCodes.AuthorityUnavailable,
                "Tenant and workspace context is required for Product authority evaluation.",
                0,
                ProductAuthorityPolicyVersions.PolicyIdentifier,
                ProductAuthorityPolicyVersions.PolicyVersion,
                now);
        }

        if (request.RequireAuthenticatedActorMatch
            && !context.PrincipalSubject.Value.Equals(actorSubject, StringComparison.Ordinal))
        {
            var mismatchResult = ProductAuthorityEvaluationResult.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Requested Product actor does not match the authenticated request context.",
                0,
                ProductAuthorityPolicyVersions.PolicyIdentifier,
                ProductAuthorityPolicyVersions.PolicyVersion,
                now);
            await RecordAuthorityEvaluationAsync(
                request,
                mismatchResult,
                context,
                actorSubject,
                resourceType,
                resourceId,
                request.ProductCapability ?? permissionKey,
                cancellationToken);
            return mismatchResult;
        }

        var actor = await dbContext.ProductActors
            .SingleOrDefaultAsync(item => item.Subject == actorSubject, cancellationToken);
        var tenantMembership = actor is null
            ? null
            : await dbContext.ProductTenantMemberships
                .Where(item => item.ActorSubject == actorSubject)
                .OrderByDescending(item => item.State == ProductMembershipStates.Active)
                .ThenByDescending(item => item.GrantedAt)
                .FirstOrDefaultAsync(cancellationToken);
        var workspaceMembership = actor is null
            ? null
            : await dbContext.ProductWorkspaceMemberships
                .Where(item => item.ActorSubject == actorSubject)
                .OrderByDescending(item => item.State == ProductMembershipStates.Active)
                .ThenByDescending(item => item.GrantedAt)
                .FirstOrDefaultAsync(cancellationToken);
        var assignment = actor is null
            ? null
            : await dbContext.ProductPermissionAssignments
            .Where(item =>
                item.ActorSubject == actorSubject
                && item.PermissionKey == permissionKey
                && (item.ResourceType == ProductAuthorityResourceTypes.Any || item.ResourceType == resourceType)
                && (item.ResourceId == ProductAuthorityResourceIds.Any || item.ResourceId == resourceId))
            .OrderByDescending(item => item.State == ProductPermissionAssignmentStates.Active)
            .ThenByDescending(item => item.AssignedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var elevation = actor is null
            ? null
            : await dbContext.ProductAuthorityElevations
                .Where(item =>
                    item.ActorSubject == actorSubject
                    && item.PermissionKey == permissionKey
                    && (item.ResourceType == ProductAuthorityResourceTypes.Any || item.ResourceType == resourceType)
                    && (item.ResourceId == ProductAuthorityResourceIds.Any || item.ResourceId == resourceId))
                .OrderByDescending(item => item.State == ProductAuthorityElevationStates.Active)
                .ThenByDescending(item => item.ApprovedAt)
                .FirstOrDefaultAsync(cancellationToken);
        var capabilityKey = request.ProductCapability ?? permissionKey;
        var hasEffectiveEntitlement = !request.RequireEntitlement
            || await dbContext.CapabilityEntitlements
                .AnyAsync(
                    entitlement =>
                        entitlement.CapabilityKey == capabilityKey
                        && entitlement.EffectiveFrom <= now
                        && (!entitlement.EffectiveTo.HasValue || entitlement.EffectiveTo.Value > now),
                    cancellationToken);

        var decision = ProductAuthorityPolicy.CanUsePermission(new ProductAuthorityPolicyInput(
            actorSubject,
            actorType,
            permissionKey,
            resourceType,
            resourceId,
            request.ProductCapability,
            request.Action,
            request.ProductRole,
            request.Classification,
            request.LifecycleState,
            request.CapturedAuthorityVersion,
            request.CapturedAt,
            request.MaximumAuthorityAge,
            request.RequireEntitlement,
            hasEffectiveEntitlement,
            request.ExternalStrongAuthenticationSatisfied,
            actor,
            tenantMembership,
            workspaceMembership,
            assignment,
            elevation,
            now));

        var result = FromDecision(decision, now);
        await RecordAuthorityEvaluationAsync(
            request,
            result,
            context,
            actorSubject,
            resourceType,
            resourceId,
            capabilityKey,
            cancellationToken);
        return result;
    }

    public async Task<ProductAuthorityEvaluationResult> EvaluateSeparationOfDutyAsync(
        ProductSeparationOfDutyRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var decision = ProductAuthorityPolicy.CanSatisfySeparationOfDuty(
            request.ActorSubject.Trim(),
            request.ConflictingActorSubject.Trim(),
            request.DutyConflict.Trim());

        var result = FromDecision(decision, now);
        await RecordSeparationOfDutyEvaluationAsync(request, result, now, cancellationToken);
        return result;
    }

    private static ProductAuthorityEvaluationResult FromDecision(
        ProductAuthorityPolicyDecision decision,
        DateTimeOffset evaluatedAt)
    {
        return decision.Succeeded
            ? ProductAuthorityEvaluationResult.Allowed(
                decision.EffectivePermission ?? string.Empty,
                decision.AuthoritySource,
                decision.AuthorityVersion,
                decision.PolicyIdentifier,
                decision.PolicyVersion,
                evaluatedAt)
            : ProductAuthorityEvaluationResult.Denied(
                decision.DenialReasonCode,
                decision.Reason ?? "Product authority denied.",
                decision.AuthorityVersion,
                decision.PolicyIdentifier,
                decision.PolicyVersion,
                evaluatedAt);
    }

    private async Task RecordAuthorityEvaluationAsync(
        ProductAuthorityEvaluationRequest request,
        ProductAuthorityEvaluationResult result,
        RequestContext context,
        string actorSubject,
        string resourceType,
        string resourceId,
        string capabilityKey,
        CancellationToken cancellationToken)
    {
        var auditRecord = new ProductAuthorityAuditRecord(
            context.TenantId,
            context.WorkspaceId,
            actorSubject,
            "IdentityAccess.ProductAuthority",
            result.Succeeded ? "ProductAuthority.Evaluated" : "ProductAuthority.Denied",
            resourceType,
            resourceId,
            request.CorrelationId ?? context.CorrelationId,
            request.CausationId ?? "ProductAuthority.Evaluate",
            result.Succeeded ? "Permit" : "Deny",
            JsonSerializer.Serialize(new
            {
                request.ActorType,
                Permission = request.PermissionKey,
                Capability = capabilityKey,
                request.Action,
                request.ProductRole,
                result.EffectivePermission,
                result.AuthoritySource,
                result.AuthorityVersion,
                result.PolicyIdentifier,
                result.PolicyVersion,
                result.DenialReasonCode
            }),
            result.EvaluatedAt);

        await RecordAuditAsync(auditRecord, result.Succeeded, cancellationToken);
    }

    private async Task RecordSeparationOfDutyEvaluationAsync(
        ProductSeparationOfDutyRequest request,
        ProductAuthorityEvaluationResult result,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Current;
        if (context is null)
        {
            return;
        }

        var auditRecord = new ProductAuthorityAuditRecord(
            context.TenantId,
            context.WorkspaceId,
            request.ActorSubject,
            "IdentityAccess.ProductAuthority",
            result.Succeeded
                ? "ProductAuthority.SeparationOfDutiesEvaluated"
                : "ProductAuthority.SeparationOfDutiesDenied",
            "ProductAuthority",
            request.DutyConflict,
            context.CorrelationId,
            "ProductAuthority.SeparationOfDuty",
            result.Succeeded ? "Permit" : "Deny",
            JsonSerializer.Serialize(new
            {
                ConflictingActorSubject = request.ConflictingActorSubject,
                request.DutyConflict,
                result.PolicyIdentifier,
                result.PolicyVersion,
                result.DenialReasonCode
            }),
            evaluatedAt);

        await RecordAuditAsync(auditRecord, result.Succeeded, cancellationToken);
    }

    private async Task RecordAuditAsync(
        ProductAuthorityAuditRecord auditRecord,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        if (succeeded)
        {
            auditStore.AddTransactionalAudit(dbContext, auditRecord);
            return;
        }

        await auditStore.PersistDurableDenialAsync(auditRecord, cancellationToken);
    }
}