using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Commercial;

public sealed class CapabilityEntitlement : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string CapabilityKey { get; init; } = string.Empty;

    public string PlanKey { get; init; } = string.Empty;

    public DateTimeOffset EffectiveFrom { get; init; }

    public DateTimeOffset? EffectiveTo { get; init; }
}