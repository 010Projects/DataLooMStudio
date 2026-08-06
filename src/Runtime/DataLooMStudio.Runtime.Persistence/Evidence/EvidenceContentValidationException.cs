namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceContentValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("Evidence content request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}