using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Modules.Tenancy;

public sealed class TenancyModule : IDataLooMModule
{
    public ModuleManifest Manifest { get; } = new(
        "Tenancy",
        "1.0.0",
        ModuleBoundaryKind.Core,
        RequiresTenantContext: false,
        RequiresWorkspaceContext: false,
        OwnsTransactionalOutbox: true,
        ContainsAiExecution: false,
        ["Authoritative tenant records", "External identity authority mapping"],
        []);
}