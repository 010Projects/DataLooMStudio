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

public sealed record LegalHoldReleaseRequestResult(
    Guid ReleaseRequestId,
    Guid LegalHoldId,
    EvidenceId EvidenceId,
    string State,
    DateTimeOffset RequestedAt,
    bool IdempotentReplay);

public sealed record LegalHoldReleaseApprovalResult(
    Guid ReleaseRequestId,
    Guid LegalHoldId,
    EvidenceId EvidenceId,
    string State,
    DateTimeOffset ReleasedAt,
    bool EvidenceUnderLegalHold,
    bool EvidencePhysicallyDeleted,
    bool IdempotentReplay);

public sealed record DeletionEligibilityResult(
    Guid EvaluationId,
    EvidenceId EvidenceId,
    bool IsEligible,
    string ReasonCode,
    string Reason,
    DateTimeOffset RetentionCommencedAt,
    DateTimeOffset? RetentionExpiresAt,
    bool HasActiveLegalHold,
    string LifecycleState,
    bool EvidencePhysicallyDeleted,
    bool IdempotentReplay);