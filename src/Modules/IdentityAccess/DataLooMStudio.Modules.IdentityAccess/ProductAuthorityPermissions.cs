namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthorityPermissions
{
    public const string ManageEvidenceReviewAssignments = "EvidenceReview.Assignments.Manage";

    public const string CreateEvidenceCandidateDecision = "EvidenceReview.CandidateDecision.Create";

    public const string ApplyEvidenceDecision = "EvidenceReview.Decision.Apply";

    public const string ManageProductPermissionAssignments = "IdentityAccess.PermissionAssignments.Manage";

    public static bool IsSupported(string permissionKey)
    {
        return permissionKey.Equals(ManageEvidenceReviewAssignments, StringComparison.Ordinal)
            || permissionKey.Equals(CreateEvidenceCandidateDecision, StringComparison.Ordinal)
            || permissionKey.Equals(ApplyEvidenceDecision, StringComparison.Ordinal)
            || permissionKey.Equals(ManageProductPermissionAssignments, StringComparison.Ordinal);
    }

    public static bool IsEvidenceReviewAssignmentPermission(string permissionKey)
    {
        return permissionKey.Equals(CreateEvidenceCandidateDecision, StringComparison.Ordinal)
            || permissionKey.Equals(ApplyEvidenceDecision, StringComparison.Ordinal);
    }
}