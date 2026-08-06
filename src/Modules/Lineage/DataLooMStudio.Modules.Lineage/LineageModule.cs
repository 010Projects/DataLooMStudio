using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Lineage;

public sealed class LineageModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Lineage",
        "1.0.0",
        ModuleBoundaryKind.Lineage,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Immutable lineage identifiers", "Versioned relationships"],
        ["Tenancy", "Workspaces"]);
}