namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed class RetentionGovernanceConflictException(string message) : Exception(message);