using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceVersion : IWorkspaceScoped
{
    public EvidenceVersionId Id { get; init; } = EvidenceVersionId.New();

    public EvidenceId EvidenceId { get; init; }

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public int Sequence { get; init; }

    public string OriginalFileName { get; init; } = string.Empty;

    public string MediaType { get; init; } = "application/octet-stream";

    public long DeclaredSize { get; init; }

    public string ContentHash { get; init; } = string.Empty;

    public string StorageObjectReference { get; init; } = string.Empty;

    public string IntegrityState { get; init; } = "Pending";

    public DateTimeOffset CreatedAt { get; init; }

    public string CreatedBy { get; init; } = "system";

    public EvidenceVersionId? SupersedesVersionId { get; init; }
}