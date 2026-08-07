using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

using DataLooMStudio.Infrastructure.Configuration;

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
        var current = options.CurrentValue;
        var serviceClient = CreateServiceClient(current);
        var containerClient = serviceClient.GetBlobContainerClient(current.EvidenceContainerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

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
        sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        var uriBuilder = new UriBuilder(blobClient.Uri)
        {
            Query = sasBuilder.ToSasQueryParameters(userDelegationKey, serviceClient.AccountName).ToString()
        };

        return new EvidenceUploadAuthority(
            storageObjectReference,
            uriBuilder.Uri.ToString(),
            request.ExpiresAt,
            "Write",
            request.MaxSize,
            request.MediaType);
    }

    public async Task<EvidenceObjectMetadata> GetMetadataAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        var blobClient = CreateBlobClient(storageObjectReference);

        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new EvidenceObjectMetadata(
                Exists: true,
                ContentLength: properties.Value.ContentLength,
                MediaType: properties.Value.ContentType,
                TrustedSha256Hash: null);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return new EvidenceObjectMetadata(
                Exists: false,
                ContentLength: 0,
                MediaType: null,
                TrustedSha256Hash: null);
        }
    }

    public async Task<Stream> OpenReadAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        var blobClient = CreateBlobClient(storageObjectReference);
        return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public async Task QuarantineAsync(
        string storageObjectReference,
        string reason,
        CancellationToken cancellationToken)
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

    public async Task RemoveUncommittedAsync(
        string storageObjectReference,
        CancellationToken cancellationToken)
    {
        var blobClient = CreateBlobClient(storageObjectReference);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
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
        var blobName = ExtractBlobName(storageObjectReference);
        return containerClient.GetBlobClient(blobName);
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

    private static string ExtractBlobName(string storageObjectReference)
    {
        const string prefix = "azblob://evidence/";
        if (!storageObjectReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage object reference is not an Azure evidence object reference.");
        }

        return storageObjectReference[prefix.Length..];
    }

    private static string NormalizeTagValue(string value)
    {
        var normalized = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(128)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? "unspecified" : normalized;
    }
}