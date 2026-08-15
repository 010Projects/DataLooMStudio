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
}