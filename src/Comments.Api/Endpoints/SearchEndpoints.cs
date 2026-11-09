using Comments.Api.Hosting;
using Comments.Application.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Comments.Api.Endpoints;

internal static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", SearchAsync)
            .WithTags("Search")
            .RequireRateLimiting(RateLimiting.SearchPolicy)
            .WithSummary("Full-text search over all comments (Elasticsearch), 20 per page. Snippets are HTML-escaped with matches in <mark>.");
        return app;
    }

    private static async Task<Results<Ok<SearchResult>, ProblemHttpResult>> SearchAsync(
        ICommentSearch search,
        ILogger<ICommentSearch> logger,
        CancellationToken cancellationToken,
        [FromQuery] string? q,
        [FromQuery] int? page)
    {
        try
        {
            return TypedResults.Ok(await search.SearchAsync(q ?? string.Empty, page ?? 1, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Search failed");
            return TypedResults.Problem("Search is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
