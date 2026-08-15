using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.IdentityAccess;

public sealed class ProductAuthorityElevation : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public Guid ActorId { get; init; }

    public string ActorSubject { get; init; } = string.Empty;

    public string ElevationType { get; init; } = ProductAuthorityElevationTypes.PrivilegedAccess;

    public string RequestedCapability { get; init; } = string.Empty;

    public string PermissionKey { get; init; } = string.Empty;

    public string ResourceType { get; init; } = ProductAuthorityResourceTypes.Any;

    public string ResourceId { get; init; } = ProductAuthorityResourceIds.Any;

    public string Reason { get; init; } = string.Empty;

    public string State { get; set; } = ProductAuthorityElevationStates.Requested;

    public long AuthorityVersion { get; set; } = 1;

    public string RequestedBy { get; init; } = string.Empty;

    public DateTimeOffset RequestedAt { get; init; }

    public string? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset EffectiveFrom { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedBy { get; set; }

    public bool RequiresExternalStrongAuthentication { get; init; }

    public bool SecurityNotificationRequired { get; init; } = true;

    public bool PostEventReviewRequired { get; init; } = true;

    public string CorrelationId { get; init; } = string.Empty;

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}