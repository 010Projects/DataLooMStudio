namespace DataLooMStudio.Modules.Evidence;

public static class EvidenceReviewPolicy
{
    public static EvidenceReviewPolicyDecision CanRecordReviewAssignment(string reviewerSubject, string permissionKey)
    {
        if (!IsHumanActor(reviewerSubject))
        {
            return EvidenceReviewPolicyDecision.Denied("Reviewer assignment requires a named human subject.");
        }

        if (string.IsNullOrWhiteSpace(permissionKey))
        {
            return EvidenceReviewPolicyDecision.Denied("Review assignment requires a canonical permission key.");
        }

        return EvidenceReviewPolicyDecision.Allowed();
    }

    public static EvidenceReviewPolicyDecision CanCreateCandidate(
        string actor,
        EvidenceReviewerAssignment? assignment,
        EvidenceReviewRequest review)
    {
        if (!IsHumanActor(actor))
        {
            return EvidenceReviewPolicyDecision.Denied("Evidence review decisions require a named human actor.");
        }

        if (EvidenceReviewStates.IsTerminal(review.State))
        {
            return EvidenceReviewPolicyDecision.Denied("Evidence review is already in an authoritative final state.");
        }

        if (assignment is null || !assignment.IsActive || !assignment.ReviewerSubject.Equals(actor, StringComparison.Ordinal))
        {
            return EvidenceReviewPolicyDecision.Denied("Only the assigned Evidence reviewer may create a candidate decision.");
        }

        if (string.IsNullOrWhiteSpace(assignment.PermissionKey))
        {
            return EvidenceReviewPolicyDecision.Denied("Assignment does not name a canonical permission key.");
        }

        return EvidenceReviewPolicyDecision.Allowed();
    }

    public static bool IsHumanActor(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        return !subject.Equals("system", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("shared:", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("group:", StringComparison.OrdinalIgnoreCase)
            && !subject.Contains("@shared", StringComparison.OrdinalIgnoreCase);
    }
}