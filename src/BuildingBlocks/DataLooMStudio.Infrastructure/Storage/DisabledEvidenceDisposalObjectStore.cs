namespace DataLooMStudio.Infrastructure.Storage;

public sealed class DisabledEvidenceDisposalObjectStore : IEvidenceDisposalObjectStore
{
    public Task<EvidenceDisposalObjectResult> DisposeEvidenceContentAsync(
        EvidenceDisposalObjectRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new EvidenceDisposalObjectResult(
            EvidenceDisposalObjectOutcomes.Suspended,
            "DisabledByAuthorityBoundary",
            EvidencePhysicallyDeleted: false,
            "Physical Evidence disposal execution is disabled until explicit production authority is granted."));
    }

    public Task<EvidenceDisposalReconciliationResult> ReconcileEvidenceContentAsync(
        EvidenceDisposalReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new EvidenceDisposalReconciliationResult(
            Confirmed: false,
            ResurrectionDetected: false,
            EvidencePhysicallyDeleted: false,
            "Physical Evidence disposal reconciliation is disabled because no destructive execution occurred."));
    }
}