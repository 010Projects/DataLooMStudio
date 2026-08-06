using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Commercial;

public sealed class CommercialModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Commercial",
        "1.0.0",
        ModuleBoundaryKind.Commercial,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Commercial capability entitlement", "Plan and feature boundaries"],
        ["Tenancy", "Workspaces"]);
}