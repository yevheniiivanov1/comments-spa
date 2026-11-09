namespace Comments.Api.Hosting;

internal static class UnknownRoutes
{
    /// <summary>
    /// Answers 404 for unknown paths under the API prefixes. Without this the SPA fallback would serve index.html with
    /// 200 for, say, <c>GET /api/comments/abc</c>. Catch-all routes rank below every specific route, so they only catch misses.
    /// </summary>
    public static void MapUnknownRoutesToNotFound(this IEndpointRouteBuilder app, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            app.Map($"{prefix}/{{**path}}", () => Results.NotFound()).ExcludeFromDescription();
        }
    }
}
