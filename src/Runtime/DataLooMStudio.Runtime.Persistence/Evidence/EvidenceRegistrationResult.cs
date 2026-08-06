using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed record EvidenceRegistrationResult(
    EvidenceId EvidenceId,
    EvidenceVersionId VersionId,
    LineageId LineageId);