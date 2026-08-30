using System.Collections.Concurrent;

namespace DataLooMStudio.Infrastructure.Storage;

public sealed class DevelopmentEvidenceObjectStore : IEvidenceObjectStore
{
    private readonly ConcurrentDictionary<string, StoredEvidenceObject> objects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StoredEvidenceObject> sealedObjects = new(StringComparer.Ordinal);
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
            "Create",
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
        return Task.FromResult(
            quarantineReasons.ContainsKey(storageObjectReference)
            || quarantineReasons.Keys.Any(reference =>
                reference.StartsWith($"{storageObjectReference}?versionid=", StringComparison.Ordinal)));
    }

    public Task<SealedEvidenceObject> SealAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        if (!objects.TryGetValue(storageObjectReference, out var stored))
        {
            return Task.FromResult(new SealedEvidenceObject(
                Exists: false,
                StorageObjectReference: storageObjectReference,
                VersionId: null,
                EntityTag: null,
                ContentLength: 0,
                MediaType: null,
                TrustedSha256Hash: null));
        }

        var versionId = Guid.NewGuid().ToString("N");
        var entityTag = $"\"{Guid.NewGuid():N}\"";
        var sealedReference = $"{storageObjectReference}?versionid={versionId}";
        sealedObjects[sealedReference] = new StoredEvidenceObject(stored.Content.ToArray(), stored.MediaType);

        return Task.FromResult(new SealedEvidenceObject(
            Exists: true,
            StorageObjectReference: sealedReference,
            VersionId: versionId,
            EntityTag: entityTag,
            ContentLength: stored.Content.Length,
            MediaType: stored.MediaType,
            TrustedSha256Hash: null));
    }

    public Task<Stream> OpenReadAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        if (!sealedObjects.TryGetValue(storageObjectReference, out var stored)
            && !objects.TryGetValue(storageObjectReference, out stored))
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

    private sealed record StoredEvidenceObject(byte[] Content, string MediaType);
}