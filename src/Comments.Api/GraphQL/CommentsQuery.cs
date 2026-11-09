using Comments.Application.Abstractions;
using Comments.Application.Comments;

namespace Comments.Api.GraphQL;

/// <summary>
/// GraphQL read API over the same use cases as REST (same cache, same validation of paging).
/// Writes stay on REST because posting is a multipart upload with a CAPTCHA.
/// </summary>
public sealed class CommentsQuery
{
    /// <summary>Top-level comments, 25 per page; newest first unless sorted otherwise.</summary>
    public Task<CommentPage> GetComments(
        [Service] CommentQueries queries,
        int page = 1,
        CommentSort sort = CommentSort.CreatedAt,
        SortDirection direction = SortDirection.Desc,
        CancellationToken cancellationToken = default) =>
        queries.GetPageAsync(page, sort, direction, cancellationToken);

    public Task<CommentDto?> GetComment(long id, [Service] CommentQueries queries, CancellationToken cancellationToken) =>
        queries.GetAsync(id, cancellationToken);

    /// <summary>Replies of a thread in id order (parents before children); pass nextAfter as after for the next page.</summary>
    public Task<ThreadPage> GetThread(long rootId, [Service] CommentQueries queries, long after = 0, CancellationToken cancellationToken = default) =>
        queries.GetThreadAsync(rootId, Math.Max(0, after), cancellationToken);

    /// <summary>Full-text search (Elasticsearch); snippets are HTML-escaped with matches in &lt;mark&gt;.</summary>
    public Task<SearchResult> Search(string query, [Service] ICommentSearch search, int page = 1, CancellationToken cancellationToken = default) =>
        search.SearchAsync(query, page, cancellationToken);
}
