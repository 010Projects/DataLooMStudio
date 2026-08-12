using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.IdentityAccess;

public sealed class IdentityAccessModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "IdentityAccess",
        "1.0.0",
        ModuleBoundaryKind.IdentityAccess,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Product actor registry", "Canonical permission assignments", "Separation-of-duty authority policy"],
        ["Tenancy", "Workspaces", "Audit", "Lineage"]);
}