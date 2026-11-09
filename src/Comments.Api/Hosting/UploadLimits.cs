namespace Comments.Api.Hosting;

internal static class UploadLimits
{
    /// <summary>The largest accepted image (5 MB) plus room for the form fields.</summary>
    public const long MaxRequestBytes = 6 * 1024 * 1024;

    /// <summary>The text is at most 10,000 characters; 64 KB covers it as JSON with room to spare.</summary>
    public const long MaxPreviewRequestBytes = 64 * 1024;
}
