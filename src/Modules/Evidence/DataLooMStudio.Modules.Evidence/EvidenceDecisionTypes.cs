namespace DataLooMStudio.Modules.Evidence;

public static class EvidenceDecisionTypes
{
    public const string Accept = "Accept";

    public const string Reject = "Reject";

    public const string RequestCorrection = "RequestCorrection";

    public const string Supersede = "Supersede";

    public static bool IsSupported(string decisionType)
    {
        return decisionType.Equals(Accept, StringComparison.Ordinal)
            || decisionType.Equals(Reject, StringComparison.Ordinal)
            || decisionType.Equals(RequestCorrection, StringComparison.Ordinal)
            || decisionType.Equals(Supersede, StringComparison.Ordinal);
    }

    public static string ToReviewState(string decisionType)
    {
        return decisionType switch
        {
            Accept => EvidenceReviewStates.Accepted,
            Reject => EvidenceReviewStates.Rejected,
            RequestCorrection => EvidenceReviewStates.CorrectionRequested,
            Supersede => EvidenceReviewStates.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(decisionType), decisionType, "Unsupported Evidence decision type.")
        };
    }

    public static string ToCandidateState(string decisionType)
    {
        return decisionType switch
        {
            Accept => EvidenceCandidateDecisionStates.Accepted,
            Reject => EvidenceCandidateDecisionStates.Rejected,
            RequestCorrection => EvidenceCandidateDecisionStates.CorrectionRequested,
            Supersede => EvidenceCandidateDecisionStates.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(decisionType), decisionType, "Unsupported Evidence decision type.")
        };
    }
}