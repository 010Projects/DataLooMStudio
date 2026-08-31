using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Infrastructure.Storage;

public interface IEvidenceObjectStore
{
    Task<EvidenceUploadAuthority> AllocateUploadAsync(
        EvidenceUploadAuthorityRequest request,
        CancellationToken cancellationToken);

    Task<SealedEvidenceObject> SealAsync(
        string storageObjectReference,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        string storageObjectReference,
        CancellationToken cancellationToken);

    Task QuarantineAsync(
        string storageObjectReference,
        string reason,
        CancellationToken cancellationToken);

}

public sealed record EvidenceUploadAuthorityRequest(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    EvidenceId EvidenceId,
    EvidenceVersionId VersionId,
    Guid AllocationId,
    DateTimeOffset ExpiresAt,
    long MaxSize,
    string MediaType);

public sealed record EvidenceUploadAuthority(
    string StorageObjectReference,
    string UploadAuthority,
    DateTimeOffset ExpiresAt,
    string PermittedOperation,
    long MaxSize,
    string MediaType);

public sealed record SealedEvidenceObject(
    bool Exists,
    string StorageObjectReference,
    string? VersionId,
    string? EntityTag,
    long ContentLength,
    string? MediaType,
    string? TrustedSha256Hash);