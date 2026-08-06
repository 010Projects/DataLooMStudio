namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceRegistrationConflictException(string message) : InvalidOperationException(message);