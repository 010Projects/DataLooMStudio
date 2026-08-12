using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Evidence",
        "1.0.0",
        ModuleBoundaryKind.EvidenceConsistency,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Evidence metadata", "Evidence integrity proof", "Evidence review and decision authority", "ADR-014 consistency boundary"],
        ["Tenancy", "Workspaces", "Lineage", "Retention", "Audit"]);
}