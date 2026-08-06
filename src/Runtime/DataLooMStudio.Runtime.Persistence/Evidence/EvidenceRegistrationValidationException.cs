namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceRegistrationValidationException(
    IReadOnlyDictionary<string, string[]> errors) : ArgumentException("Evidence registration command is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}