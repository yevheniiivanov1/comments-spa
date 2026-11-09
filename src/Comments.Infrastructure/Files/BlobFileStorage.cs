using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Comments.Application.Abstractions;
using Comments.Domain.Comments;

namespace Comments.Infrastructure.Files;

/// <summary>
/// Attachments in Azure Blob Storage (Azurite locally). The container is private; files are served through the API,
/// which sets the content type and security headers itself.
/// </summary>
internal sealed class BlobFileStorage(BlobServiceClient client) : IFileStorage
{
    public const string ContainerName = "attachments";

    private readonly Lazy<Task<BlobContainerClient>> _container = new(async () =>
    {
        var container = client.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);
        return container;
    });

    public async Task<string> SaveAsync(ProcessedFile file, CancellationToken cancellationToken)
    {
        var folder = file.Kind == AttachmentKind.Image ? "images" : "texts";
        var key = $"{folder}/{Guid.CreateVersion7():N}{file.Extension}";

        var container = await _container.Value;
        await container.GetBlobClient(key).UploadAsync(
            BinaryData.FromBytes(file.Content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType } },
            cancellationToken);
        return key;
    }

    public async Task<StoredFile?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        var container = await _container.Value;
        try
        {
            var response = await container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: cancellationToken);
            var details = response.Value.Details;
            return new StoredFile(response.Value.Content, details.ContentType, details.ContentLength);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var container = await _container.Value;
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}
