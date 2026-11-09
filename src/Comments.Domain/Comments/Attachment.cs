namespace Comments.Domain.Comments;

/// <summary>A file attached to a comment. The bytes live in blob storage under <see cref="StorageKey"/>.</summary>
public abstract class Attachment
{
    public const int MaxFileNameLength = 255;

    protected Attachment()
    {
    }

    protected Attachment(string fileName, string storageKey, string contentType, long sizeBytes)
    {
        Id = Guid.CreateVersion7();
        FileName = Shorten(Path.GetFileName(fileName));
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }

    public Guid Id { get; private set; }

    public long CommentId { get; private set; }

    /// <summary>The name the file was uploaded with; shown to readers, never used as a path.</summary>
    public string FileName { get; private set; } = null!;

    public string StorageKey { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public abstract AttachmentKind Kind { get; }

    private static string Shorten(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "file";
        }

        return name.Length <= MaxFileNameLength ? name : name[^MaxFileNameLength..];
    }
}

public enum AttachmentKind
{
    Image = 1,
    Text = 2,
}

/// <summary>A JPG, GIF or PNG image, at most 320×240 after proportional downscaling.</summary>
public sealed class ImageAttachment : Attachment
{
    public const int MaxWidth = 320;
    public const int MaxHeight = 240;

    private ImageAttachment()
    {
    }

    public ImageAttachment(string fileName, string storageKey, string contentType, long sizeBytes, int width, int height, bool wasResized)
        : base(fileName, storageKey, contentType, sizeBytes)
    {
        if (width is < 1 or > MaxWidth || height is < 1 or > MaxHeight)
        {
            throw new ArgumentException($"Image must be within {MaxWidth}×{MaxHeight}.");
        }

        Width = width;
        Height = height;
        WasResized = wasResized;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool WasResized { get; private set; }

    public override AttachmentKind Kind => AttachmentKind.Image;
}

/// <summary>A plain-text (.txt) file up to 100 KB.</summary>
public sealed class TextAttachment : Attachment
{
    public const int MaxSizeBytes = 100 * 1024;

    private TextAttachment()
    {
    }

    public TextAttachment(string fileName, string storageKey, long sizeBytes)
        : base(fileName, storageKey, "text/plain; charset=utf-8", sizeBytes)
    {
        if (sizeBytes is < 0 or > MaxSizeBytes)
        {
            throw new ArgumentException($"Text file must be at most {MaxSizeBytes / 1024} KB.");
        }
    }

    public override AttachmentKind Kind => AttachmentKind.Text;
}
