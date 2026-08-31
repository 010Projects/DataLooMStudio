using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public interface IEvidenceQueryService
{
    Task<EvidenceSummary> GetAsync(EvidenceId evidenceId, CancellationToken cancellationToken);
}

public sealed record EvidenceSummary(
    EvidenceId EvidenceId,
    EvidenceVersionId VersionId,
    string EvidenceType,
    string Classification,
    string LifecycleState,
    string VerificationStatus,
    string OriginalFileName,
    string MediaType,
    long ContentLength,
    string Sha256Hash,
    DateTimeOffset CapturedAt,
    string LineageId);

public sealed class EvidenceQueryForbiddenException(string message) : Exception(message);