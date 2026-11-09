namespace Comments.Api.Hosting;

internal static class UploadLimits
{
    /// <summary>The largest accepted image (5 MB) plus room for the form fields.</summary>
    public const long MaxRequestBytes = 6 * 1024 * 1024;
}
