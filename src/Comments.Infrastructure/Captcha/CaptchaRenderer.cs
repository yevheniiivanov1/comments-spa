using SkiaSharp;

namespace Comments.Infrastructure.Captcha;

/// <summary>Draws the CAPTCHA text with per-letter rotation, jitter and noise lines into a PNG.</summary>
internal sealed class CaptchaRenderer : IDisposable
{
    public const int Width = 180;
    public const int Height = 60;

    private static readonly string[] PreferredFamilies = ["DejaVu Sans", "Liberation Sans", "Arial", "Segoe UI", "Helvetica"];

    private readonly SKTypeface _typeface = ResolveTypeface();

    public byte[] Render(string text)
    {
        var random = Random.Shared;
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xF2, 0xF4, 0xF8));

        using var noise = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        for (var i = 0; i < 9; i++)
        {
            noise.Color = RandomColor(random, 120, 200);
            canvas.DrawLine(random.Next(Width), random.Next(Height), random.Next(Width), random.Next(Height), noise);
        }

        using var font = new SKFont(_typeface, 34) { Embolden = true };
        using var ink = new SKPaint { IsAntialias = true };
        var x = 14f;
        foreach (var letter in text)
        {
            ink.Color = RandomColor(random, 20, 110);
            canvas.Save();
            canvas.Translate(x, 42 + random.Next(-5, 6));
            canvas.RotateDegrees(random.Next(-28, 29));
            canvas.DrawText(letter.ToString(), 0, 0, SKTextAlign.Left, font, ink);
            canvas.Restore();
            x += font.MeasureText(letter.ToString()) + random.Next(2, 7);
        }

        using var dots = new SKPaint { IsAntialias = true };
        for (var i = 0; i < 160; i++)
        {
            dots.Color = RandomColor(random, 60, 200);
            canvas.DrawCircle(random.Next(Width), random.Next(Height), 0.9f, dots);
        }

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    public void Dispose() => _typeface.Dispose();

    private static SKColor RandomColor(Random random, int min, int max) =>
        new((byte)random.Next(min, max), (byte)random.Next(min, max), (byte)random.Next(min, max));

    /// <summary>Uses an installed sans-serif font (the Docker image installs DejaVu); falls back to Skia's default face.</summary>
    private static SKTypeface ResolveTypeface()
    {
        foreach (var family in PreferredFamilies)
        {
            var typeface = SKTypeface.FromFamilyName(family, SKFontStyle.Bold);
            if (typeface is not null && string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return typeface;
            }

            typeface?.Dispose();
        }

        return SKTypeface.Default;
    }
}
