using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceReviewRequest : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public EvidenceVersionId EvidenceVersionId { get; init; }

    public LineageId LineageId { get; init; } = LineageId.New();

    public string ReviewKind { get; init; } = "EvidenceReview";

    public string State { get; set; } = EvidenceReviewStates.Requested;

    public int Version { get; set; } = 1;

    public string RequestedBy { get; init; } = string.Empty;

    public DateTimeOffset RequestedAt { get; init; }

    public DateTimeOffset? DueAt { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}