using Comments.Api.Hosting;
using Comments.Application.Comments;
using Comments.Domain.Comments;
using Comments.Domain.Markup;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Comments.Api.Endpoints;

internal static class CommentEndpoints
{
    public static IEndpointRouteBuilder MapCommentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/comments").WithTags("Comments");

        group.MapGet("/", GetPageAsync)
            .WithSummary("Top-level comments, 25 per page. Default order: newest first (LIFO).");

        group.MapGet("/{id:long}", GetAsync)
            .WithSummary("One comment.");

        group.MapGet("/{rootId:long}/thread", GetThreadAsync)
            .WithSummary("Replies of a thread in id order (parents before children), up to 200 per page; pass nextAfter as 'after' for more.");

        group.MapPost("/", PostAsync)
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimiting.PostPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(UploadLimits.MaxRequestBytes))
            .WithSummary("Posts a comment or a reply (multipart/form-data, optional 'file').");

        group.MapPost("/preview", Preview)
            .RequireRateLimiting(RateLimiting.PreviewPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(UploadLimits.MaxPreviewRequestBytes))
            .WithSummary("Validates the text and returns the XHTML that would be published.");

        return app;
    }

    /// <summary>Sort and direction are matched case-insensitively (<c>userName</c>, <c>email</c>, <c>createdAt</c>; <c>asc</c>, <c>desc</c>).</summary>
    private static async Task<Results<Ok<CommentPage>, ValidationProblem>> GetPageAsync(
        CommentQueries queries,
        CancellationToken cancellationToken,
        [FromQuery] int? page,
        [FromQuery] string? sort,
        [FromQuery] string? direction)
    {
        var errors = new Dictionary<string, string[]>();
        var sortBy = ParseOrDefault(sort, CommentSort.CreatedAt, "sort", errors);
        var order = ParseOrDefault(direction, SortDirection.Desc, "direction", errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await queries.GetPageAsync(page ?? 1, sortBy, order, cancellationToken));
    }

    private static T ParseOrDefault<T>(string? value, T fallback, string name, Dictionary<string, string[]> errors) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors[name] = [$"Use one of: {string.Join(", ", Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]))}."];
        return fallback;
    }

    private static async Task<Results<Ok<CommentDto>, NotFound>> GetAsync(long id, CommentQueries queries, CancellationToken cancellationToken) =>
        await queries.GetAsync(id, cancellationToken) is { } comment ? TypedResults.Ok(comment) : TypedResults.NotFound();

    private static async Task<Ok<ThreadPage>> GetThreadAsync(long rootId, CommentQueries queries, CancellationToken cancellationToken, [FromQuery] long? after) =>
        TypedResults.Ok(await queries.GetThreadAsync(rootId, Math.Max(0, after ?? 0), cancellationToken));

    private static async Task<Results<Created<CommentDto>, ValidationProblem>> PostAsync(
        [FromForm] PostCommentForm form,
        IFormFile? file,
        HttpContext http,
        PostCommentHandler handler,
        CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream();
        var command = new PostCommentCommand(
            form.UserName,
            form.Email,
            form.HomePage,
            form.Text,
            form.ParentId,
            form.CaptchaId,
            form.CaptchaAnswer,
            file is null ? null : new Application.Abstractions.FileUpload(file.FileName, file.Length, content!),
            ClientInfo.Create(http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString()));

        var result = await handler.HandleAsync(command, cancellationToken);
        return result.Succeeded
            ? TypedResults.Created($"/api/comments/{result.Comment!.Id}", result.Comment)
            : TypedResults.ValidationProblem(result.Errors!.ToDictionary(e => e.Key, e => e.Value));
    }

    private static Ok<PreviewResponse> Preview(PreviewRequest request)
    {
        var result = CommentMarkup.Parse(request.Text);
        return TypedResults.Ok(new PreviewResponse(result.Html, result.Errors));
    }
}

internal sealed class PostCommentForm
{
    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string? HomePage { get; set; }

    public string? Text { get; set; }

    public long? ParentId { get; set; }

    public string? CaptchaId { get; set; }

    public string? CaptchaAnswer { get; set; }
}

internal sealed record PreviewRequest(string? Text);

internal sealed record PreviewResponse(string? Html, IReadOnlyList<string> Errors);
