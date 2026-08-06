using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Lifecycle;

public sealed class LifecycleRecord : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string SubjectType { get; init; } = string.Empty;

    public string SubjectId { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public int Version { get; init; } = 1;

    public DateTimeOffset ChangedAt { get; init; }
}