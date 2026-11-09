namespace Comments.Api.Hosting;

/// <summary>
/// Response headers against XSS, clickjacking and MIME sniffing. The page's CSP allows scripts only from this origin,
/// so even markup that slipped through validation could not run inline script. Tool UIs (/graphql, /scalar) load their
/// own assets and are left without the page policy.
/// </summary>
internal static class SecurityHeaders
{
    private const string PagePolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private static readonly string[] ToolPaths = ["/graphql", "/scalar", "/openapi"];

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (!ToolPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                headers.ContentSecurityPolicy = PagePolicy;
            }

            await next(context);
        });
}
