namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthorityActions
{
    public const string EvidenceRegister = "Evidence.Register";

    public const string EvidenceRead = "Evidence.Read";

    public const string ReviewAssignmentManage = "EvidenceReview.Assignment.Manage";

    public const string CandidateDecisionCreate = "EvidenceReview.CandidateDecision.Create";

    public const string DecisionApply = "EvidenceReview.Decision.Apply";

    public const string RetentionPolicyManage = "Governance.Retention.Manage";

    public const string LegalHoldManage = "Governance.LegalHold.Manage";

    public const string LegalHoldReleaseRequest = "Governance.LegalHold.Release.Request";

    public const string LegalHoldReleaseApprove = "Governance.LegalHold.Release.Approve";

    public const string DeletionEligibilityEvaluate = "Governance.Retention.DeletionEligibility.Evaluate";

    public const string EvidenceDisposalRequest = "Evidence.Disposal.Request";

    public const string EvidenceDisposalApprove = "Evidence.Disposal.Approve";

    public const string EvidenceDisposalQueue = "Evidence.Disposal.Queue";

    public const string EvidenceDisposalExecute = "Workload.EvidenceDisposal.Execute";

    public const string EvidenceDisposalReconcile = "Workload.EvidenceDisposal.Reconcile";

    public const string SupportDiagnosticsRead = "Support.Diagnostics.Read";

    public const string WorkloadProcess = "Workload.Process";
}