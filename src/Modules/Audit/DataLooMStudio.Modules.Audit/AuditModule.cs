using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Audit;

public sealed class AuditModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Audit",
        "1.0.0",
        ModuleBoundaryKind.Audit,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Append-only audit entries", "Actor and correlation traceability"],
        ["Tenancy", "Workspaces"]);
}