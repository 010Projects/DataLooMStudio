using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.IdentityAccess;

public sealed class ProductPermissionAssignment : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public Guid ActorId { get; init; }

    public string ActorSubject { get; init; } = string.Empty;

    public string PermissionKey { get; init; } = string.Empty;

    public string ResourceType { get; init; } = ProductAuthorityResourceTypes.Any;

    public string ResourceId { get; init; } = ProductAuthorityResourceIds.Any;

    public string State { get; set; } = ProductPermissionAssignmentStates.Active;

    public string AssignedBy { get; init; } = string.Empty;

    public DateTimeOffset AssignedAt { get; init; }

    public DateTimeOffset? EffectiveFrom { get; init; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}