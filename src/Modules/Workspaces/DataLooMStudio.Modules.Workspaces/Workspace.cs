using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Workspaces;

public sealed class Workspace : ITenantScoped
{
    public WorkspaceId Id { get; init; } = WorkspaceId.New();

    public TenantId TenantId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DataResidencyRegion { get; init; } = string.Empty;

    public string LifecycleState { get; init; } = "Active";

    public string CreatedBy { get; init; } = "system";

    public DateTimeOffset CreatedAt { get; init; }

    public Guid ConcurrencyToken { get; init; } = Guid.NewGuid();
}