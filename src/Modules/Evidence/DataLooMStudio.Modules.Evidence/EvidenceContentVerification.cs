using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceContentVerification : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public EvidenceVersionId VersionId { get; init; }

    public Guid AllocationId { get; init; }

    public string StorageObjectReference { get; init; } = string.Empty;

    public string StorageVersionId { get; init; } = string.Empty;

    public string StorageEntityTag { get; init; } = string.Empty;

    public string ReceiptIdempotencyKey { get; init; } = string.Empty;

    public string ReceiptRequestHash { get; init; } = string.Empty;

    public long DeclaredSize { get; init; }

    public long ActualSize { get; init; }

    public string ExpectedSha256Hash { get; init; } = string.Empty;

    public string ActualSha256Hash { get; init; } = string.Empty;

    public string IntegrityOutcome { get; init; } = "NotRun";

    public string ScanOutcome { get; init; } = "NotRun";

    public string ScannerName { get; init; } = string.Empty;

    public string ScannerVersion { get; init; } = string.Empty;

    public string ResultLifecycleState { get; init; } = "Quarantined";

    public string? FailureReason { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    public DateTimeOffset VerifiedAt { get; init; }

    public DateTimeOffset? ScannedAt { get; init; }
}