using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using DataLooMStudio.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace DataLooMStudio.Infrastructure.Storage;

public sealed class AzureEvidenceBlobStore(
    IOptionsMonitor<DataLooMInfrastructureOptions> options,
    TokenCredential credential) : IEvidenceBlobStore
{
    public async Task<Uri> UploadAsync(
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(current.BlobServiceUri))
        {
            throw new InvalidOperationException("Blob service URI is not configured.");
        }

        var serviceClient = new BlobServiceClient(new Uri(current.BlobServiceUri), credential);
        var containerClient = serviceClient.GetBlobContainerClient(current.EvidenceContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);

        return blobClient.Uri;
    }
}