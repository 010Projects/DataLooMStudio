using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Retention;

public sealed class DeletionEligibilityEvaluation : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public EvidenceId EvidenceId { get; init; }

    public Guid? RetentionPolicyId { get; init; }

    public string RetentionPolicyKey { get; init; } = string.Empty;

    public DateTimeOffset RetentionCommencedAt { get; init; }

    public DateTimeOffset? RetentionExpiresAt { get; init; }

    public bool HasActiveLegalHold { get; init; }

    public string LifecycleState { get; init; } = string.Empty;

    public bool IsEligible { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public string EvaluatedBy { get; init; } = string.Empty;

    public DateTimeOffset EvaluatedAt { get; init; }

    public long AuthorityVersion { get; init; }

    public string PolicyIdentifier { get; init; } = string.Empty;

    public int PolicyVersion { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestHash { get; init; } = string.Empty;

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}