namespace Comments.Application.Abstractions;

/// <summary>Blob storage for attachments. Keys look like <c>images/{guid}.png</c> and are never derived from user input.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(ProcessedFile file, CancellationToken cancellationToken);

    Task<StoredFile?> OpenAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed record StoredFile(Stream Content, string ContentType, long Length);
