using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Retention;

public sealed class RetentionModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Retention",
        "1.0.0",
        ModuleBoundaryKind.Retention,
        RequiresTenantContext: true,
        RequiresWorkspaceContext: true,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Retention policies", "Legal holds", "Deletion eligibility decisions", "Governed disposal decisions"],
        ["Tenancy", "Workspaces", "Evidence"]);
}