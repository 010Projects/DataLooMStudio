namespace DataLooMStudio.Modules.Evidence;

public static class EvidenceReviewAuthorityRoles
{
    public const string Reviewer = "EvidenceReviewer";

    public const string Approver = "EvidenceApprover";

    public static bool IsEvidenceReviewRole(string role)
    {
        return role.Equals(Reviewer, StringComparison.Ordinal)
            || role.Equals(Approver, StringComparison.Ordinal);
    }
}