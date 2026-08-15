namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed class RetentionGovernanceValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("Retention governance request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}