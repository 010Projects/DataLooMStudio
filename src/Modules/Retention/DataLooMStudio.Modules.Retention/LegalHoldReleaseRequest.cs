using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Retention;

public sealed class LegalHoldReleaseRequest : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public Guid LegalHoldId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public string State { get; set; } = LegalHoldReleaseStates.Pending;

    public string RequestedBy { get; init; } = string.Empty;

    public string RequestReason { get; init; } = string.Empty;

    public DateTimeOffset RequestedAt { get; init; }

    public long RequestAuthorityVersion { get; init; }

    public string RequestPolicyIdentifier { get; init; } = string.Empty;

    public int RequestPolicyVersion { get; init; }

    public string? ApprovedBy { get; set; }

    public string? ApprovalReason { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public long? ApprovalAuthorityVersion { get; set; }

    public string? ApprovalPolicyIdentifier { get; set; }

    public int? ApprovalPolicyVersion { get; set; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public string? ApprovalIdempotencyKey { get; set; }

    public string? ApprovalRequestHash { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}