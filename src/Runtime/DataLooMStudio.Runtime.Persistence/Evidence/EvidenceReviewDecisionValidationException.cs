namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceReviewDecisionValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Evidence review decision request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}