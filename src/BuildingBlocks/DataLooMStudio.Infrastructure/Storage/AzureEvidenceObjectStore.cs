using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.Infrastructure.Observability;

using Microsoft.Extensions.Options;

namespace DataLooMStudio.Infrastructure.Storage;

public sealed class AzureEvidenceObjectStore(
    IOptionsMonitor<DataLooMInfrastructureOptions> options,
    TokenCredential credential) : IEvidenceObjectStore
{
    public async Task<EvidenceUploadAuthority> AllocateUploadAsync(
        EvidenceUploadAuthorityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = options.CurrentValue;
            var serviceClient = CreateServiceClient(current);
            var containerClient = serviceClient.GetBlobContainerClient(current.EvidenceContainerName);

            var storageObjectReference = BuildStorageObjectReference(request);
            var blobName = ExtractBlobName(storageObjectReference);
            var blobClient = containerClient.GetBlobClient(blobName);
            var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
            var userDelegationKey = await serviceClient.GetUserDelegationKeyAsync(
                startsOn,
                request.ExpiresAt,
                cancellationToken);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                BlobName = blobClient.Name,
                Resource = "b",
                StartsOn = startsOn,
                ExpiresOn = request.ExpiresAt,
                ContentType = request.MediaType
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Create);

            var uriBuilder = new UriBuilder(blobClient.Uri)
            {
                Query = sasBuilder.ToSasQueryParameters(userDelegationKey, serviceClient.AccountName).ToString()
            };

            return new EvidenceUploadAuthority(
                storageObjectReference,
                uriBuilder.Uri.ToString(),
                request.ExpiresAt,
                "Create",
                request.MaxSize,
                request.MediaType);
        }
        catch (RequestFailedException)
        {
            InfrastructureTelemetry.RecordDependencyFailure("blob_storage", "allocate_upload");
            throw;
        }
    }

    public async Task<SealedEvidenceObject> SealAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        var blobClient = CreateBlobClient(storageObjectReference);

        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(properties.Value.VersionId)
                || string.IsNullOrWhiteSpace(properties.Value.ETag.ToString()))
            {
                throw new InvalidOperationException(
                    "Evidence content cannot be sealed because Blob versioning or entity-tag evidence is unavailable.");
            }

            var sealedReference = BuildVersionedReference(storageObjectReference, properties.Value.VersionId);
            return new SealedEvidenceObject(
                Exists: true,
                StorageObjectReference: sealedReference,
                VersionId: properties.Value.VersionId,
                EntityTag: properties.Value.ETag.ToString(),
                ContentLength: properties.Value.ContentLength,
                MediaType: properties.Value.ContentType,
                TrustedSha256Hash: null);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return new SealedEvidenceObject(
                Exists: false,
                StorageObjectReference: storageObjectReference,
                VersionId: null,
                EntityTag: null,
                ContentLength: 0,
                MediaType: null,
                TrustedSha256Hash: null);
        }
        catch (RequestFailedException)
        {
            InfrastructureTelemetry.RecordDependencyFailure("blob_storage", "seal");
            throw;
        }
    }

    public async Task<Stream> OpenReadAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var blobClient = CreateBlobClient(storageObjectReference);
            return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException)
        {
            InfrastructureTelemetry.RecordDependencyFailure("blob_storage", "read");
            throw;
        }
    }

    public async Task QuarantineAsync(
        string storageObjectReference,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var blobClient = CreateBlobClient(storageObjectReference);
            await blobClient.SetTagsAsync(
                new Dictionary<string, string>
                {
                    ["dls-quarantine"] = "true",
                    ["dls-quarantine-reason"] = NormalizeTagValue(reason)
                },
                cancellationToken: cancellationToken);
        }
        catch (RequestFailedException)
        {
            InfrastructureTelemetry.RecordDependencyFailure("blob_storage", "quarantine");
            throw;
        }
    }

    private BlobServiceClient CreateServiceClient(DataLooMInfrastructureOptions current)
    {
        if (string.IsNullOrWhiteSpace(current.BlobServiceUri))
        {
            throw new InvalidOperationException("Blob service URI is not configured.");
        }

        return new BlobServiceClient(new Uri(current.BlobServiceUri), credential);
    }

    private BlobClient CreateBlobClient(string storageObjectReference)
    {
        var current = options.CurrentValue;
        var serviceClient = CreateServiceClient(current);
        var containerClient = serviceClient.GetBlobContainerClient(current.EvidenceContainerName);
        var (blobName, versionId) = ParseStorageObjectReference(storageObjectReference);
        var blobClient = containerClient.GetBlobClient(blobName);
        return string.IsNullOrWhiteSpace(versionId) ? blobClient : blobClient.WithVersion(versionId);
    }

    private static string BuildStorageObjectReference(EvidenceUploadAuthorityRequest request)
    {
        return string.Join(
            '/',
            "azblob://evidence",
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
    }

    private static string ExtractBlobName(string storageObjectReference) =>
        ParseStorageObjectReference(storageObjectReference).BlobName;

    private static (string BlobName, string? VersionId) ParseStorageObjectReference(string storageObjectReference)
    {
        if (!Uri.TryCreate(storageObjectReference, UriKind.Absolute, out var reference)
            || !reference.Scheme.Equals("azblob", StringComparison.OrdinalIgnoreCase)
            || !reference.Host.Equals("evidence", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage object reference is not an Azure evidence object reference.");
        }

        var blobName = reference.AbsolutePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new InvalidOperationException("Storage object reference does not contain a Blob name.");
        }

        if (string.IsNullOrWhiteSpace(reference.Query))
        {
            return (blobName, null);
        }

        const string versionPrefix = "?versionid=";
        if (!reference.Query.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase)
            || reference.Query.Contains('&', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage object reference contains an unsupported query boundary.");
        }

        var versionId = Uri.UnescapeDataString(reference.Query[versionPrefix.Length..]);
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new InvalidOperationException("Storage object reference contains an empty Blob version id.");
        }

        return (blobName, versionId);
    }

    private static string BuildVersionedReference(string storageObjectReference, string versionId) =>
        $"{storageObjectReference}?versionid={Uri.EscapeDataString(versionId)}";

    private static string NormalizeTagValue(string value)
    {
        var normalized = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(128)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? "unspecified" : normalized;
    }
}