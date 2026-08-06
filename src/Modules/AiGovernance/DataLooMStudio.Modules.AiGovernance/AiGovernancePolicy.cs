using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.AiGovernance;

public sealed class AiGovernancePolicy : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string PolicyKey { get; init; } = string.Empty;

    public bool AllowsModelExecution { get; init; }

    public string ExecutionAuthority { get; init; } = "OutsideEngineering";

    public DateTimeOffset CreatedAt { get; init; }
}