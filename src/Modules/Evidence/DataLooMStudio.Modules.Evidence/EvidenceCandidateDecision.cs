using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceCandidateDecision : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public Guid ReviewRequestId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public EvidenceVersionId EvidenceVersionId { get; init; }

    public string DecisionType { get; init; } = EvidenceDecisionTypes.Accept;

    public string State { get; set; } = EvidenceCandidateDecisionStates.Candidate;

    public int Version { get; set; } = 1;

    public string Summary { get; init; } = string.Empty;

    public Guid? SupersedesDecisionId { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public string? AppliedBy { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public string? AppliedReason { get; set; }

    public string? AppliedIdempotencyKey { get; set; }

    public string? AppliedRequestHash { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}