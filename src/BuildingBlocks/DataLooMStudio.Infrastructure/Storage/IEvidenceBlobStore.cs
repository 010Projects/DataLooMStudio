namespace DataLooMStudio.Infrastructure.Storage;

public interface IEvidenceBlobStore
{
    Task<Uri> UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken);
}