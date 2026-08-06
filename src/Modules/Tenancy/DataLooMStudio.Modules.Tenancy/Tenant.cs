using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Tenancy;

public sealed class Tenant
{
    public TenantId Id { get; init; } = TenantId.New();

    public string DisplayName { get; init; } = string.Empty;

    public string ExternalAuthority { get; init; } = string.Empty;

    public string LifecycleState { get; init; } = "Active";

    public string CreatedBy { get; init; } = "system";

    public DateTimeOffset CreatedAt { get; init; }

    public Guid ConcurrencyToken { get; init; } = Guid.NewGuid();
}