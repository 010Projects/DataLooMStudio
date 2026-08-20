namespace DataLooMStudio.Runtime.Persistence.Retention;

public interface IRetentionGovernanceService
{
    Task<RetentionPolicyResult> DefineRetentionPolicyAsync(
        RetentionPolicyCommand command,
        CancellationToken cancellationToken);

    Task<LegalHoldResult> PlaceLegalHoldAsync(
        PlaceLegalHoldCommand command,
        CancellationToken cancellationToken);

    Task<LegalHoldReleaseRequestResult> RequestLegalHoldReleaseAsync(
        LegalHoldReleaseRequestCommand command,
        CancellationToken cancellationToken);

    Task<LegalHoldReleaseApprovalResult> ApproveLegalHoldReleaseAsync(
        LegalHoldReleaseApprovalCommand command,
        CancellationToken cancellationToken);

    Task<DeletionEligibilityResult> EvaluateDeletionEligibilityAsync(
        DeletionEligibilityCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceDisposalResult> RequestEvidenceDisposalAsync(
        EvidenceDisposalRequestCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceDisposalResult> ApproveEvidenceDisposalAsync(
        EvidenceDisposalApprovalCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceDisposalResult> QueueEvidenceDisposalAsync(
        EvidenceDisposalQueueCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceDisposalResult> ExecuteEvidenceDisposalAsync(
        EvidenceDisposalExecutionCommand command,
        CancellationToken cancellationToken);

    Task<EvidenceDisposalResult> ReconcileEvidenceDisposalAsync(
        EvidenceDisposalReconciliationCommand command,
        CancellationToken cancellationToken);
}