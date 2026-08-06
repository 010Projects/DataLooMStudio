using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Lifecycle;

public sealed class LifecycleModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Lifecycle",
        "1.0.0",
        ModuleBoundaryKind.Lifecycle,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["State definitions", "State transition auditability"],
        ["Tenancy", "Workspaces", "Audit"]);
}