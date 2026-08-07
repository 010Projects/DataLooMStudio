using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceUploadAllocation : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public EvidenceVersionId VersionId { get; init; }

    public string StorageObjectReference { get; init; } = string.Empty;

    public string UploadAuthorityHash { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; init; }

    public string PermittedOperation { get; init; } = "Write";

    public long MaxSize { get; init; }

    public string MediaType { get; init; } = "application/octet-stream";

    public string Status { get; set; } = "Active";

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public string CreatedBy { get; init; } = "system";

    public DateTimeOffset? ConsumedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}