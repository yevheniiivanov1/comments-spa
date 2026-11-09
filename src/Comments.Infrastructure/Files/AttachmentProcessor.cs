using System.Text.Unicode;
using Comments.Application.Abstractions;
using Comments.Domain.Comments;
using SkiaSharp;

namespace Comments.Infrastructure.Files;

/// <summary>
/// Validates uploads by content, not by name.
/// <para>
/// Images: the bytes must decode as JPG, PNG or GIF. Anything larger than 320×240 is scaled down proportionally
/// (EXIF rotation applied) and re-encoded; JPG and PNG are always re-encoded, which also drops metadata such as GPS tags.
/// Skia has no GIF encoder, so a GIF that fits is kept as uploaded (animation survives) and a larger one becomes a PNG of its first frame.
/// </para>
/// <para>
/// Memory: large JPEGs are decoded directly at a reduced size, at most 24 MP are accepted, and only a few images are decoded
/// at a time, so a burst of big uploads cannot exhaust the API's memory.
/// </para>
/// <para>Text: a .txt file up to 100 KB without NUL bytes, stored as is; the encoding is UTF-8 if the bytes are valid UTF-8, otherwise Windows-1251.</para>
/// </summary>
internal sealed class AttachmentProcessor : IAttachmentProcessor
{
    public const long MaxImageUploadBytes = 5 * 1024 * 1024;
    private const long MaxSourcePixels = 24_000_000;

    private static readonly SemaphoreSlim DecodeSlots = new(Math.Max(2, Environment.ProcessorCount / 2));
    private const int JpegQuality = 90;

    public const string UnsupportedTypeError = "Only JPG, GIF and PNG images or TXT files can be attached.";

    public async Task<AttachmentProcessingResult> ProcessAsync(FileUpload upload, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" => await ProcessImageAsync(upload, cancellationToken),
            ".txt" => await ProcessTextAsync(upload, cancellationToken),
            _ => AttachmentProcessingResult.Fail(UnsupportedTypeError),
        };
    }

    private static async Task<AttachmentProcessingResult> ProcessImageAsync(FileUpload upload, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(upload, MaxImageUploadBytes, cancellationToken);
        if (bytes is null)
        {
            return AttachmentProcessingResult.Fail($"Image must be at most {MaxImageUploadBytes / 1024 / 1024} MB.");
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Gif))
        {
            return AttachmentProcessingResult.Fail("The file is not a valid JPG, GIF or PNG image.");
        }

        var info = codec.Info;
        if ((long)info.Width * info.Height > MaxSourcePixels)
        {
            return AttachmentProcessingResult.Fail("Image dimensions are too large.");
        }

        var origin = codec.EncodedOrigin;
        var (width, height) = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom
            ? (info.Height, info.Width)
            : (info.Width, info.Height);
        var fits = width <= ImageAttachment.MaxWidth && height <= ImageAttachment.MaxHeight;

        if (codec.EncodedFormat == SKEncodedImageFormat.Gif && fits)
        {
            return AttachmentProcessingResult.Ok(new ProcessedFile(
                AttachmentKind.Image, upload.FileName, "image/gif", ".gif", bytes, width, height));
        }

        // Decoding is the memory-hungry part (4 bytes per pixel), so only a few uploads decode at once.
        await DecodeSlots.WaitAsync(cancellationToken);
        try
        {
            return Scale(codec, origin, width, height, upload.FileName);
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    private static AttachmentProcessingResult Scale(SKCodec codec, SKEncodedOrigin origin, int width, int height, string fileName)
    {
        var scale = Math.Min(1d, Math.Min((double)ImageAttachment.MaxWidth / width, (double)ImageAttachment.MaxHeight / height));
        var targetWidth = Math.Clamp((int)Math.Round(width * scale), 1, ImageAttachment.MaxWidth);
        var targetHeight = Math.Clamp((int)Math.Round(height * scale), 1, ImageAttachment.MaxHeight);

        using var decoded = DecodeAtLeast(codec, scale);
        if (decoded is null)
        {
            return AttachmentProcessingResult.Fail("The file is not a valid JPG, GIF or PNG image.");
        }

        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var oriented = origin == SKEncodedOrigin.TopLeft
            ? decoded
            : Orient(decoded, origin, swaps ? decoded.Height : decoded.Width, swaps ? decoded.Width : decoded.Height);
        SKBitmap? resized = null;
        try
        {
            if (oriented.Width != targetWidth || oriented.Height != targetHeight)
            {
                resized = oriented.Resize(
                    new SKImageInfo(targetWidth, targetHeight, oriented.ColorType, oriented.AlphaType),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            var (format, contentType, extension) = codec.EncodedFormat == SKEncodedImageFormat.Jpeg
                ? (SKEncodedImageFormat.Jpeg, "image/jpeg", ".jpg")
                : (SKEncodedImageFormat.Png, "image/png", ".png");

            using var output = new MemoryStream();
            if (!(resized ?? oriented).Encode(output, format, JpegQuality))
            {
                return AttachmentProcessingResult.Fail("The image could not be processed.");
            }

            return AttachmentProcessingResult.Ok(new ProcessedFile(
                AttachmentKind.Image, fileName, contentType, extension, output.ToArray(), targetWidth, targetHeight, WasResized: scale < 1));
        }
        finally
        {
            resized?.Dispose();
            if (!ReferenceEquals(oriented, decoded))
            {
                oriented.Dispose();
            }
        }
    }

    /// <summary>
    /// Decodes at the smallest size the codec can produce directly that still leaves twice the target resolution for the
    /// final resample (JPEG decodes at 1/2, 1/4 or 1/8). A 24 MP photo then takes a few MB instead of ~100 MB.
    /// </summary>
    private static SKBitmap? DecodeAtLeast(SKCodec codec, double scale)
    {
        var full = codec.Info;
        if (scale < 0.5)
        {
            var size = codec.GetScaledDimensions((float)(scale * 2));
            if (size.Width < full.Width && size.Width >= full.Width * scale && size.Height >= full.Height * scale)
            {
                var scaled = SKBitmap.Decode(codec, full.WithSize(size.Width, size.Height));
                if (scaled is not null)
                {
                    return scaled;
                }
            }
        }

        return SKBitmap.Decode(codec);
    }

    private static async Task<AttachmentProcessingResult> ProcessTextAsync(FileUpload upload, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(upload, TextAttachment.MaxSizeBytes, cancellationToken);
        if (bytes is null)
        {
            return AttachmentProcessingResult.Fail($"Text file must be at most {TextAttachment.MaxSizeBytes / 1024} KB.");
        }

        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return AttachmentProcessingResult.Fail("The file is not a plain text file.");
        }

        var charset = Utf8.IsValid(bytes) ? "utf-8" : "windows-1251";
        return AttachmentProcessingResult.Ok(new ProcessedFile(
            AttachmentKind.Text, upload.FileName, $"text/plain; charset={charset}", ".txt", bytes));
    }

    /// <summary>Reads the upload, or returns null as soon as it turns out to be larger than <paramref name="limit"/>.</summary>
    private static async Task<byte[]?> ReadAsync(FileUpload upload, long limit, CancellationToken cancellationToken)
    {
        if (upload.Length > limit)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await upload.Content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin, int width, int height)
    {
        var result = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, width / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, width / 2f, height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, height / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1, 0, source.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1, 0, source.Height / 2f);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                break;
        }

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return result;
    }
}
