using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Infrastructure.Storage;

public interface IEvidenceDisposalObjectStore
{
    Task<EvidenceDisposalObjectResult> DisposeEvidenceContentAsync(
        EvidenceDisposalObjectRequest request,
        CancellationToken cancellationToken);

    Task<EvidenceDisposalReconciliationResult> ReconcileEvidenceContentAsync(
        EvidenceDisposalReconciliationRequest request,
        CancellationToken cancellationToken);
}

public sealed record EvidenceDisposalObjectRequest(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    EvidenceId EvidenceId,
    Guid DisposalRecordId,
    string StorageObjectReference,
    string ExpectedSha256Hash,
    string WorkloadIdentitySubject);

public sealed record EvidenceDisposalObjectResult(
    string Outcome,
    string Disposition,
    bool EvidencePhysicallyDeleted,
    string Reason);

public sealed record EvidenceDisposalReconciliationRequest(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    EvidenceId EvidenceId,
    Guid DisposalRecordId,
    string StorageObjectReference,
    string ExpectedSha256Hash);

public sealed record EvidenceDisposalReconciliationResult(
    bool Confirmed,
    bool ResurrectionDetected,
    bool EvidencePhysicallyDeleted,
    string Reason);

public static class EvidenceDisposalObjectOutcomes
{
    public const string Succeeded = "Succeeded";

    public const string Suspended = "Suspended";

    public const string Failed = "Failed";
}