using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public interface IEvidenceContentService
{
    Task<EvidenceUploadAllocationResult> AllocateUploadAsync(
        EvidenceUploadAllocationRequest request,
        CancellationToken cancellationToken);

    Task<EvidenceContentReceiptResult> ConfirmContentReceivedAsync(
        EvidenceContentReceiptRequest request,
        CancellationToken cancellationToken);
}

public sealed record EvidenceUploadAllocationRequest(
    EvidenceId EvidenceId,
    string? IdempotencyKey);

public sealed record EvidenceUploadAllocationResult(
    EvidenceId EvidenceId,
    EvidenceVersionId VersionId,
    Guid AllocationId,
    string StorageObjectReference,
    string UploadAuthority,
    DateTimeOffset ExpiresAt,
    string PermittedOperation,
    long MaxSize,
    string MediaType,
    bool IdempotentReplay);

public sealed record EvidenceContentReceiptRequest(
    EvidenceId EvidenceId,
    EvidenceVersionId VersionId,
    string StorageObjectReference,
    string? IdempotencyKey);

public sealed record EvidenceContentReceiptResult(
    EvidenceId EvidenceId,
    EvidenceVersionId VersionId,
    string LifecycleState,
    string IntegrityOutcome,
    string ScanOutcome,
    string? FailureReason,
    long ActualSize,
    string ActualSha256Hash,
    DateTimeOffset VerifiedAt,
    bool IdempotentReplay);