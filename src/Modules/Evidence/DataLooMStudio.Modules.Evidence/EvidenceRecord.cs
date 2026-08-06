using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Evidence;

public sealed class EvidenceRecord : IWorkspaceScoped
{
    public EvidenceId Id { get; init; } = EvidenceId.New();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public LineageId LineageId { get; init; } = LineageId.New();

    public EvidenceVersionId CurrentVersionId { get; init; }

    public string EvidenceType { get; init; } = "Unspecified";

    public string Classification { get; init; } = "Internal";

    public string LifecycleState { get; init; } = "Registered";

    public string RegisteredBy { get; init; } = "system";

    public string BlobName { get; init; } = string.Empty;

    public string ContentType { get; init; } = "application/octet-stream";

    public long ContentLength { get; init; }

    public string Sha256Hash { get; init; } = string.Empty;

    public EvidenceVerificationStatus VerificationStatus { get; init; } = EvidenceVerificationStatus.Pending;

    public int Version { get; init; } = 1;

    public bool IsImmutable { get; init; } = true;

    public bool IsUnderLegalHold { get; init; }

    public string RetentionPolicyKey { get; init; } = string.Empty;

    public string RegistrationIdempotencyKey { get; init; } = string.Empty;

    public string RegistrationRequestHash { get; init; } = string.Empty;

    public DateTimeOffset CapturedAt { get; init; }

    public Guid ConcurrencyToken { get; init; } = Guid.NewGuid();
}