using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Workflows;

public sealed class WorkflowsModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Workflows",
        "1.0.0",
        ModuleBoundaryKind.Workflow,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Workflow definitions", "Workflow run tracking", "No lifecycle ownership"],
        ["Tenancy", "Workspaces", "Lifecycle", "Audit"]);
}