namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthorityPermissions
{
    public const string RegisterEvidence = "Evidence.Register";

    public const string ReadEvidence = "Evidence.Read";

    public const string ReadRestrictedEvidence = "Evidence.Read.Restricted";

    public const string ManageEvidenceReviewAssignments = "EvidenceReview.Assignments.Manage";

    public const string CreateEvidenceCandidateDecision = "EvidenceReview.CandidateDecision.Create";

    public const string ApplyEvidenceDecision = "EvidenceReview.Decision.Apply";

    public const string ManageProductPermissionAssignments = "IdentityAccess.PermissionAssignments.Manage";

    public const string ReadSupportDiagnostics = "Support.Diagnostics.Read";

    public const string ActivateSupportElevation = "Support.Elevation.Activate";

    public const string ActivateBreakGlass = "Security.BreakGlass.Activate";

    public const string ManageRetentionPolicy = "Governance.Retention.Manage";

    public const string ManageLegalHold = "Governance.LegalHold.Manage";

    public const string RequestLegalHoldRelease = "Governance.LegalHold.Release.Request";

    public const string ApproveLegalHoldRelease = "Governance.LegalHold.Release.Approve";

    public const string EvaluateDeletionEligibility = "Governance.Retention.DeletionEligibility.Evaluate";

    public const string RequestEvidenceDisposal = "Evidence.Disposal.Request";

    public const string ApproveEvidenceDisposal = "Evidence.Disposal.Approve";

    public const string QueueEvidenceDisposal = "Evidence.Disposal.Queue";

    public const string ExecuteEvidenceDisposal = "Workload.EvidenceDisposal.Execute";

    public const string ReconcileEvidenceDisposal = "Workload.EvidenceDisposal.Reconcile";

    public const string ProcessOutbox = "Workload.Outbox.Process";

    public const string ScanEvidenceContent = "Workload.EvidenceContent.Scan";

    public const string ReconcileOutbox = "Workload.Outbox.Reconcile";

    public static bool IsSupported(string permissionKey)
    {
        return permissionKey.Equals(RegisterEvidence, StringComparison.Ordinal)
            || permissionKey.Equals(ReadEvidence, StringComparison.Ordinal)
            || permissionKey.Equals(ReadRestrictedEvidence, StringComparison.Ordinal)
            || permissionKey.Equals(ManageEvidenceReviewAssignments, StringComparison.Ordinal)
            || permissionKey.Equals(CreateEvidenceCandidateDecision, StringComparison.Ordinal)
            || permissionKey.Equals(ApplyEvidenceDecision, StringComparison.Ordinal)
            || permissionKey.Equals(ManageProductPermissionAssignments, StringComparison.Ordinal)
            || permissionKey.Equals(ReadSupportDiagnostics, StringComparison.Ordinal)
            || permissionKey.Equals(ActivateSupportElevation, StringComparison.Ordinal)
            || permissionKey.Equals(ActivateBreakGlass, StringComparison.Ordinal)
            || permissionKey.Equals(ManageRetentionPolicy, StringComparison.Ordinal)
            || permissionKey.Equals(ManageLegalHold, StringComparison.Ordinal)
            || permissionKey.Equals(RequestLegalHoldRelease, StringComparison.Ordinal)
            || permissionKey.Equals(ApproveLegalHoldRelease, StringComparison.Ordinal)
            || permissionKey.Equals(EvaluateDeletionEligibility, StringComparison.Ordinal)
            || permissionKey.Equals(RequestEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(ApproveEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(QueueEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(ExecuteEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(ReconcileEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(ProcessOutbox, StringComparison.Ordinal)
            || permissionKey.Equals(ScanEvidenceContent, StringComparison.Ordinal)
            || permissionKey.Equals(ReconcileOutbox, StringComparison.Ordinal);
    }

    public static bool IsEvidenceReviewAssignmentPermission(string permissionKey)
    {
        return permissionKey.Equals(CreateEvidenceCandidateDecision, StringComparison.Ordinal)
            || permissionKey.Equals(ApplyEvidenceDecision, StringComparison.Ordinal);
    }

    public static bool IsEvidenceContentPermission(string permissionKey)
    {
        return permissionKey.Equals(RegisterEvidence, StringComparison.Ordinal)
            || permissionKey.Equals(ReadEvidence, StringComparison.Ordinal)
            || permissionKey.Equals(ReadRestrictedEvidence, StringComparison.Ordinal);
    }

    public static bool IsEvidenceReviewOrDecisionPermission(string permissionKey)
    {
        return permissionKey.Equals(ManageEvidenceReviewAssignments, StringComparison.Ordinal)
            || permissionKey.Equals(CreateEvidenceCandidateDecision, StringComparison.Ordinal)
            || permissionKey.Equals(ApplyEvidenceDecision, StringComparison.Ordinal);
    }

    public static bool IsRetentionOrLegalHoldPermission(string permissionKey)
    {
        return permissionKey.Equals(ManageRetentionPolicy, StringComparison.Ordinal)
            || permissionKey.Equals(ManageLegalHold, StringComparison.Ordinal)
            || permissionKey.Equals(RequestLegalHoldRelease, StringComparison.Ordinal)
            || permissionKey.Equals(ApproveLegalHoldRelease, StringComparison.Ordinal)
            || permissionKey.Equals(EvaluateDeletionEligibility, StringComparison.Ordinal);
    }

    public static bool IsEvidenceDisposalPermission(string permissionKey)
    {
        return permissionKey.Equals(RequestEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(ApproveEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(QueueEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(ExecuteEvidenceDisposal, StringComparison.Ordinal)
            || permissionKey.Equals(ReconcileEvidenceDisposal, StringComparison.Ordinal);
    }
}