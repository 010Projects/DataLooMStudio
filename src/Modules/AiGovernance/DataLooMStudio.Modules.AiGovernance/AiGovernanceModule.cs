using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.AiGovernance;

public sealed class AiGovernanceModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "AiGovernance",
        "1.0.0",
        ModuleBoundaryKind.AiGovernanceBoundary,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["AI policy boundary", "Model execution prohibition", "Prompt and result governance metadata only"],
        ["Tenancy", "Workspaces", "Audit", "Commercial"]);
}