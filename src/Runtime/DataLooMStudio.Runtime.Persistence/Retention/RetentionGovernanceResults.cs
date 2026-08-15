using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed record RetentionPolicyResult(
    Guid PolicyId,
    string PolicyKey,
    int RetainForDays,
    bool LegalHoldOverridesDeletion,
    DateTimeOffset CreatedAt,
    bool IdempotentReplay);

public sealed record LegalHoldResult(
    Guid LegalHoldId,
    EvidenceId EvidenceId,
    DateTimeOffset PlacedAt,
    bool EvidenceUnderLegalHold,
    bool IdempotentReplay);