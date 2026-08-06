using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Audit;

public sealed class AuditEntry : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string ActorSubject { get; init; } = string.Empty;

    public string AuthorityContext { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string TargetType { get; init; } = string.Empty;

    public string TargetId { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string CausationId { get; init; } = string.Empty;

    public string Outcome { get; init; } = "Succeeded";

    public string MetadataJson { get; init; } = "{}";

    public DateTimeOffset OccurredAt { get; init; }
}