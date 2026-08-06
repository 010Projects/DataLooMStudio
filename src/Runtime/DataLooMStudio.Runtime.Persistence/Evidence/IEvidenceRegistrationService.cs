namespace DataLooMStudio.Runtime.Persistence.Evidence;

public interface IEvidenceRegistrationService
{
    Task<EvidenceRegistrationResult> RegisterInitialVersionAsync(
        EvidenceRegistrationRequest request,
        CancellationToken cancellationToken);
}