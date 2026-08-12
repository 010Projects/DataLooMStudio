using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed record EvidenceReviewRequestCommand(
    EvidenceId EvidenceId,
    EvidenceVersionId EvidenceVersionId,
    string ReviewKind,
    DateTimeOffset? DueAt,
    string? IdempotencyKey);

public sealed record EvidenceReviewerAssignmentCommand(
    Guid ReviewId,
    string ReviewerSubject,
    string Role,
    string? IdempotencyKey);

public sealed record EvidenceCandidateDecisionCommand(
    Guid ReviewId,
    string DecisionType,
    string Summary,
    Guid? SupersedesDecisionId,
    string? IdempotencyKey);

public sealed record EvidenceApplyDecisionCommand(
    Guid ReviewId,
    Guid CandidateDecisionId,
    string DecisionType,
    int ExpectedCandidateVersion,
    string? Reason,
    string? IdempotencyKey);