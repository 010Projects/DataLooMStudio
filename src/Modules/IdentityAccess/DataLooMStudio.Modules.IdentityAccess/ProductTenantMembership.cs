using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.IdentityAccess;

public sealed class ProductTenantMembership : ITenantScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public Guid ActorId { get; init; }

    public string ActorSubject { get; init; } = string.Empty;

    public string State { get; set; } = ProductMembershipStates.Active;

    public long AuthorityVersion { get; set; } = 1;

    public string GrantedBy { get; init; } = string.Empty;

    public DateTimeOffset GrantedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedBy { get; set; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}