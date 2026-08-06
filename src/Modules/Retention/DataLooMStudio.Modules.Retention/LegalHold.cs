using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Retention;

public sealed class LegalHold : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string PlacedBy { get; init; } = string.Empty;

    public DateTimeOffset PlacedAt { get; init; }

    public DateTimeOffset? ReleasedAt { get; init; }
}