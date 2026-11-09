using Comments.Application.Abstractions;

namespace Comments.Api.Endpoints;

internal static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/files/{folder:regex(^(images|texts)$)}/{name:regex(^[0-9a-f]{{32}}\\.(jpg|png|gif|txt)$)}", GetAsync)
            .WithTags("Files")
            .WithSummary("An attachment. Keys are random, so responses are cached as immutable.");
        return app;
    }

    /// <summary>
    /// Security: the route only matches keys the server generates, the stored content type is the one the server chose
    /// after decoding the file, and the response is sandboxed (CSP) so even a crafted text file opened directly cannot run script.
    /// </summary>
    private static async Task<IResult> GetAsync(string folder, string name, IFileStorage storage, HttpContext http, CancellationToken cancellationToken)
    {
        var file = await storage.OpenAsync($"{folder}/{name}", cancellationToken);
        if (file is null)
        {
            return Results.NotFound();
        }

        var headers = http.Response.Headers;
        headers.CacheControl = "public, max-age=31536000, immutable";
        headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
        headers.ContentDisposition = "inline";
        return Results.Stream(file.Content, file.ContentType);
    }
}
