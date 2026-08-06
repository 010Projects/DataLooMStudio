using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Workflows;

public sealed class WorkflowRun : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string WorkflowKey { get; init; } = string.Empty;

    public string SubjectType { get; init; } = string.Empty;

    public string SubjectId { get; init; } = string.Empty;

    public WorkflowRunStatus Status { get; init; } = WorkflowRunStatus.Pending;

    public DateTimeOffset QueuedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}