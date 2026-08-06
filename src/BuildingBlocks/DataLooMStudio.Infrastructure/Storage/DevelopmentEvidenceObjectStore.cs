using System.Collections.Concurrent;

namespace DataLooMStudio.Infrastructure.Storage;

public sealed class DevelopmentEvidenceObjectStore : IEvidenceObjectStore
{
    private readonly ConcurrentDictionary<string, StoredEvidenceObject> objects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> quarantineReasons = new(StringComparer.Ordinal);

    public Task<EvidenceUploadAuthority> AllocateUploadAsync(
        EvidenceUploadAuthorityRequest request,
        CancellationToken cancellationToken)
    {
        var storageObjectReference = string.Join(
            '/',
            "dls-dev://evidence",
            "tenants",
            request.TenantId.Value.ToString("D"),
            "workspaces",
            request.WorkspaceId.Value.ToString("D"),
            "evidence",
            request.EvidenceId.Value.ToString("D"),
            "versions",
            request.VersionId.Value.ToString("D"),
            "allocations",
            request.AllocationId.ToString("N"));

        return Task.FromResult(new EvidenceUploadAuthority(
            storageObjectReference,
            $"dls-dev-upload:{request.AllocationId:N}:{request.ExpiresAt.ToUnixTimeSeconds()}",
            request.ExpiresAt,
            "Write",
            request.MaxSize,
            request.MediaType));
    }

    public Task StoreObjectAsync(
        string storageObjectReference,
        byte[] content,
        string mediaType,
        CancellationToken cancellationToken)
    {
        objects[storageObjectReference] = new StoredEvidenceObject(content.ToArray(), mediaType);
        return Task.CompletedTask;
    }

    public Task<bool> IsQuarantinedAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(quarantineReasons.ContainsKey(storageObjectReference));
    }

    public Task<EvidenceObjectMetadata> GetMetadataAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        if (!objects.TryGetValue(storageObjectReference, out var stored))
        {
            return Task.FromResult(new EvidenceObjectMetadata(
                Exists: false,
                ContentLength: 0,
                MediaType: null,
                TrustedSha256Hash: null));
        }

        return Task.FromResult(new EvidenceObjectMetadata(
            Exists: true,
            ContentLength: stored.Content.Length,
            MediaType: stored.MediaType,
            TrustedSha256Hash: null));
    }

    public Task<Stream> OpenReadAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        if (!objects.TryGetValue(storageObjectReference, out var stored))
        {
            throw new FileNotFoundException("Evidence object was not found.", storageObjectReference);
        }

        return Task.FromResult<Stream>(new MemoryStream(stored.Content, writable: false));
    }

    public Task QuarantineAsync(
        string storageObjectReference,
        string reason,
        CancellationToken cancellationToken)
    {
        quarantineReasons[storageObjectReference] = reason;
        return Task.CompletedTask;
    }

    public Task RemoveUncommittedAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        objects.TryRemove(storageObjectReference, out _);
        quarantineReasons.TryRemove(storageObjectReference, out _);
        return Task.CompletedTask;
    }

    private sealed record StoredEvidenceObject(byte[] Content, string MediaType);
}