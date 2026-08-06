namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed record EvidenceRegistrationRequest(
    string EvidenceType,
    string Classification,
    string OriginalFileName,
    string MediaType,
    long DeclaredSize,
    string ContentHash,
    string StorageObjectReference,
    string RetentionPolicyKey);