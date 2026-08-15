using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed record RetentionPolicyCommand(
    string PolicyKey,
    string Description,
    int RetainForDays,
    bool LegalHoldOverridesDeletion,
    string? IdempotencyKey);

public sealed record PlaceLegalHoldCommand(
    EvidenceId EvidenceId,
    string Reason,
    string? IdempotencyKey);

public sealed record LegalHoldReleaseRequestCommand(
    EvidenceId EvidenceId,
    Guid LegalHoldId,
    string Reason,
    string? IdempotencyKey);

public sealed record LegalHoldReleaseApprovalCommand(
    Guid ReleaseRequestId,
    string Reason,
    string? IdempotencyKey);

public sealed record DeletionEligibilityCommand(
    EvidenceId EvidenceId,
    string? IdempotencyKey);