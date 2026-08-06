namespace DataLooMStudio.SharedKernel.Modules;

public sealed record ModuleManifest(
    string Name,
    string Version,
    ModuleBoundaryKind BoundaryKind,
    bool RequiresTenantContext,
    bool RequiresWorkspaceContext,
    bool OwnsTransactionalOutbox,
    bool ContainsAiExecution,
    IReadOnlyList<string> Responsibilities,
    IReadOnlyList<string> DependsOn);