namespace DataLooMStudio.Runtime.Persistence.Evidence;

public interface IEvidenceReviewDecisionService
{
    Task<EvidenceReviewRequestResult> RequestReviewAsync(
        EvidenceReviewRequestCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceReviewerAssignmentResult> AssignReviewerAsync(
        EvidenceReviewerAssignmentCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceCandidateDecisionResult> CreateCandidateDecisionAsync(
        EvidenceCandidateDecisionCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceAppliedDecisionResult> ApplyDecisionAsync(
        EvidenceApplyDecisionCommand command,
        CancellationToken cancellationToken);
}