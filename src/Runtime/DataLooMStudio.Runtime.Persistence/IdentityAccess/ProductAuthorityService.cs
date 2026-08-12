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
        _ = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Product authority evaluation.");

        var actorSubject = request.ActorSubject.Trim();
        var permissionKey = request.PermissionKey.Trim();
        var resourceType = request.ResourceType.Trim();
        var resourceId = request.ResourceId.Trim();
        var actor = await dbContext.ProductActors
            .SingleOrDefaultAsync(item => item.Subject == actorSubject, cancellationToken);
        var assignment = await dbContext.ProductPermissionAssignments
            .Where(item =>
                item.ActorSubject == actorSubject
                && item.PermissionKey == permissionKey
                && item.State == ProductPermissionAssignmentStates.Active
                && (item.ResourceType == ProductAuthorityResourceTypes.Any || item.ResourceType == resourceType)
                && (item.ResourceId == ProductAuthorityResourceIds.Any || item.ResourceId == resourceId))
            .OrderByDescending(item => item.AssignedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var decision = ProductAuthorityPolicy.CanUsePermission(
            actorSubject,
            permissionKey,
            resourceType,
            resourceId,
            actor,
            assignment,
            clock.UtcNow);

        return decision.Succeeded
            ? ProductAuthorityEvaluationResult.Allowed()
            : ProductAuthorityEvaluationResult.Denied(decision.Reason!);
    }

    public Task<ProductAuthorityEvaluationResult> EvaluateSeparationOfDutyAsync(
        ProductSeparationOfDutyRequest request,
        CancellationToken cancellationToken)
    {
        var decision = ProductAuthorityPolicy.CanSatisfySeparationOfDuty(
            request.ActorSubject.Trim(),
            request.ConflictingActorSubject.Trim(),
            request.DutyConflict.Trim());

        return Task.FromResult(decision.Succeeded
            ? ProductAuthorityEvaluationResult.Allowed()
            : ProductAuthorityEvaluationResult.Denied(decision.Reason!));
    }
}