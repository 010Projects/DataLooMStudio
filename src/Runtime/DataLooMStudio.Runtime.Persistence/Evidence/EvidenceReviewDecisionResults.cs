using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed record EvidenceReviewRequestResult(
    Guid ReviewId,
    EvidenceId EvidenceId,
    EvidenceVersionId EvidenceVersionId,
    string State,
    int Version,
    DateTimeOffset RequestedAt,
    bool IdempotentReplay);

public sealed record EvidenceReviewerAssignmentResult(
    Guid AssignmentId,
    Guid ReviewId,
    string ReviewerSubject,
    string PermissionKey,
    bool IdempotentReplay);

public sealed record EvidenceCandidateDecisionResult(
    Guid CandidateDecisionId,
    Guid ReviewId,
    string DecisionType,
    string State,
    int Version,
    bool IdempotentReplay);

public sealed record EvidenceAppliedDecisionResult(
    Guid ReviewId,
    Guid CandidateDecisionId,
    string ReviewState,
    string CandidateState,
    int CandidateVersion,
    DateTimeOffset DecidedAt,
    bool IdempotentReplay);