using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Retention;

public sealed class DisposalRecord : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public Guid DeletionEligibilityEvaluationId { get; init; }

    public string RetentionPolicyKey { get; init; } = string.Empty;

    public DateTimeOffset? RetentionExpiresAt { get; init; }

    public string LifecycleState { get; init; } = string.Empty;

    public string StorageObjectReference { get; init; } = string.Empty;

    public string ExpectedSha256Hash { get; init; } = string.Empty;

    public string State { get; set; } = DisposalRecordStates.Requested;

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

    public string? QueuedBy { get; set; }

    public DateTimeOffset? QueuedAt { get; set; }

    public string? ExecutedBy { get; set; }

    public DateTimeOffset? ExecutionStartedAt { get; set; }

    public DateTimeOffset? StorageDisposedAt { get; set; }

    public long? ExecutionAuthorityVersion { get; set; }

    public string? ExecutionPolicyIdentifier { get; set; }

    public int? ExecutionPolicyVersion { get; set; }

    public string? StorageDisposition { get; set; }

    public bool EvidencePhysicallyDeleted { get; set; }

    public string? ReconciledBy { get; set; }

    public DateTimeOffset? ReconciledAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public string? LastFailureReason { get; set; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public string? ApprovalIdempotencyKey { get; set; }

    public string? ApprovalRequestHash { get; set; }

    public string? QueueIdempotencyKey { get; set; }

    public string? QueueRequestHash { get; set; }

    public string? ExecutionIdempotencyKey { get; set; }

    public string? ExecutionRequestHash { get; set; }

    public string? ReconciliationIdempotencyKey { get; set; }

    public string? ReconciliationRequestHash { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}