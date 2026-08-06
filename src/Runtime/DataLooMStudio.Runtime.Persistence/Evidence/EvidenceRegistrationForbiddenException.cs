namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceRegistrationForbiddenException(string message) : UnauthorizedAccessException(message);