using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceReviewerAssignment : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public Guid ReviewRequestId { get; init; }

    public string ReviewerSubject { get; init; } = string.Empty;

    public string Role { get; init; } = EvidenceReviewAuthorityRoles.Reviewer;

    public string AssignedBy { get; init; } = string.Empty;

    public DateTimeOffset AssignedAt { get; init; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? RemovedAt { get; set; }

    public string? RemovedBy { get; set; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}