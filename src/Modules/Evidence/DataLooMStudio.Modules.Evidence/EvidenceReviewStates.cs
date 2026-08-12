namespace DataLooMStudio.Modules.Evidence;

public static class EvidenceReviewStates
{
    public const string Requested = "Requested";

    public const string Assigned = "Assigned";

    public const string CandidateProposed = "CandidateProposed";

    public const string Accepted = "Accepted";

    public const string Rejected = "Rejected";

    public const string CorrectionRequested = "CorrectionRequested";

    public const string Superseded = "Superseded";

    public static bool IsTerminal(string state)
    {
        return state.Equals(Accepted, StringComparison.Ordinal)
            || state.Equals(Rejected, StringComparison.Ordinal)
            || state.Equals(CorrectionRequested, StringComparison.Ordinal)
            || state.Equals(Superseded, StringComparison.Ordinal);
    }
}