using Comments.Domain.Comments;

namespace Comments.Application.Abstractions;

/// <summary>Checks an uploaded file and turns it into what is stored: images re-encoded and downscaled, text converted to UTF-8.</summary>
public interface IAttachmentProcessor
{
    Task<AttachmentProcessingResult> ProcessAsync(FileUpload upload, CancellationToken cancellationToken);
}

public sealed record FileUpload(string FileName, long Length, Stream Content);

public sealed record ProcessedFile(
    AttachmentKind Kind,
    string FileName,
    string ContentType,
    string Extension,
    byte[] Content,
    int? Width = null,
    int? Height = null,
    bool WasResized = false)
{
    public Attachment ToAttachment(string storageKey) => Kind == AttachmentKind.Image
        ? new ImageAttachment(FileName, storageKey, ContentType, Content.Length, Width!.Value, Height!.Value, WasResized)
        : new TextAttachment(FileName, storageKey, ContentType, Content.Length);
}

public sealed record AttachmentProcessingResult(ProcessedFile? File, string? Error)
{
    public static AttachmentProcessingResult Ok(ProcessedFile file) => new(file, null);

    public static AttachmentProcessingResult Fail(string error) => new(null, error);
}
