using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Workspaces;

public sealed class WorkspacesModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Workspaces",
        "1.0.0",
        ModuleBoundaryKind.Core,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: false,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Workspace catalog", "Tenant-owned workspace membership boundary"],
        ["Tenancy"]);
}