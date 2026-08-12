using System.Text.Json;

using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed class ProductAuthorityService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    IClock clock) : IProductAuthorityService
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
            RecordAuthorityEvaluation(request, mismatchResult, context, actorSubject, resourceType, resourceId, request.ProductCapability ?? permissionKey);
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
        RecordAuthorityEvaluation(request, result, context, actorSubject, resourceType, resourceId, capabilityKey);
        return result;
    }

    public Task<ProductAuthorityEvaluationResult> EvaluateSeparationOfDutyAsync(
        ProductSeparationOfDutyRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var decision = ProductAuthorityPolicy.CanSatisfySeparationOfDuty(
            request.ActorSubject.Trim(),
            request.ConflictingActorSubject.Trim(),
            request.DutyConflict.Trim());

        var result = FromDecision(decision, now);
        RecordSeparationOfDutyEvaluation(request, result, now);
        return Task.FromResult(result);
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

    private void RecordAuthorityEvaluation(
        ProductAuthorityEvaluationRequest request,
        ProductAuthorityEvaluationResult result,
        RequestContext context,
        string actorSubject,
        string resourceType,
        string resourceId,
        string capabilityKey)
    {
        dbContext.AuditEntries.Add(new AuditEntry
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            ActorSubject = actorSubject,
            AuthorityContext = "IdentityAccess.ProductAuthority",
            Action = result.Succeeded ? "ProductAuthority.Evaluated" : "ProductAuthority.Denied",
            TargetType = resourceType,
            TargetId = resourceId,
            CorrelationId = request.CorrelationId ?? context.CorrelationId,
            CausationId = request.CausationId ?? "ProductAuthority.Evaluate",
            Outcome = result.Succeeded ? "Permit" : "Deny",
            MetadataJson = JsonSerializer.Serialize(new
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
            OccurredAt = result.EvaluatedAt
        });
    }

    private void RecordSeparationOfDutyEvaluation(
        ProductSeparationOfDutyRequest request,
        ProductAuthorityEvaluationResult result,
        DateTimeOffset evaluatedAt)
    {
        var context = requestContextAccessor.Current;
        if (context is null)
        {
            return;
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            ActorSubject = request.ActorSubject,
            AuthorityContext = "IdentityAccess.ProductAuthority",
            Action = result.Succeeded
                ? "ProductAuthority.SeparationOfDutiesEvaluated"
                : "ProductAuthority.SeparationOfDutiesDenied",
            TargetType = "ProductAuthority",
            TargetId = request.DutyConflict,
            CorrelationId = context.CorrelationId,
            CausationId = "ProductAuthority.SeparationOfDuty",
            Outcome = result.Succeeded ? "Permit" : "Deny",
            MetadataJson = JsonSerializer.Serialize(new
            {
                ConflictingActorSubject = request.ConflictingActorSubject,
                request.DutyConflict,
                result.PolicyIdentifier,
                result.PolicyVersion,
                result.DenialReasonCode
            }),
            OccurredAt = evaluatedAt
        });
    }
}