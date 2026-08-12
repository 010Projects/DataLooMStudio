namespace DataLooMStudio.Modules.Evidence;

public static class EvidenceDecisionPolicy
{
    public static EvidenceReviewPolicyDecision CanApplyAuthoritativeDecision(
        string actor,
        EvidenceReviewRequest review,
        EvidenceCandidateDecision candidate,
        string decisionType,
        int expectedCandidateVersion)
    {
        if (!EvidenceReviewPolicy.IsHumanActor(actor))
        {
            return EvidenceReviewPolicyDecision.Denied("Evidence decision authority requires a named human actor.");
        }

        if (!EvidenceDecisionTypes.IsSupported(decisionType))
        {
            return EvidenceReviewPolicyDecision.Denied("Evidence decision type is not supported.");
        }

        if (!candidate.DecisionType.Equals(decisionType, StringComparison.Ordinal))
        {
            return EvidenceReviewPolicyDecision.Denied("Authoritative action must match the candidate decision type.");
        }

        if (candidate.Version != expectedCandidateVersion)
        {
            return EvidenceReviewPolicyDecision.Denied("Candidate decision version is stale.");
        }

        if (!candidate.State.Equals(EvidenceCandidateDecisionStates.Candidate, StringComparison.Ordinal))
        {
            return EvidenceReviewPolicyDecision.Denied("Candidate decision is no longer pending.");
        }

        if (EvidenceReviewStates.IsTerminal(review.State))
        {
            return EvidenceReviewPolicyDecision.Denied("Evidence review is already in an authoritative final state.");
        }

        return EvidenceReviewPolicyDecision.Allowed();
    }
}