using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Comments.Application.Abstractions;
using Comments.Domain.Comments;

namespace Comments.Infrastructure.Files;

/// <summary>
/// Attachments in Azure Blob Storage (Azurite locally). The container is private; files are served through the API,
/// which sets the content type and security headers itself.
/// <para>
/// The container is created on first use. A failed attempt (storage still starting, a network blip) is not remembered:
/// the next request tries again instead of every upload failing until the process restarts.
/// </para>
/// </summary>
internal sealed class BlobFileStorage(BlobServiceClient client) : IFileStorage, IDisposable
{
    public const string ContainerName = "attachments";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private BlobContainerClient? _container;

    public void Dispose() => _gate.Dispose();

    private async Task<BlobContainerClient> ContainerAsync(CancellationToken cancellationToken)
    {
        if (_container is { } ready)
        {
            return ready;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_container is null)
            {
                var container = client.GetBlobContainerClient(ContainerName);
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                _container = container;
            }

            return _container;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> SaveAsync(ProcessedFile file, CancellationToken cancellationToken)
    {
        var folder = file.Kind == AttachmentKind.Image ? "images" : "texts";
        var key = $"{folder}/{Guid.CreateVersion7():N}{file.Extension}";

        var container = await ContainerAsync(cancellationToken);
        await container.GetBlobClient(key).UploadAsync(
            BinaryData.FromBytes(file.Content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType } },
            cancellationToken);
        return key;
    }

    public async Task<StoredFile?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken);
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
        var container = await ContainerAsync(cancellationToken);
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}
