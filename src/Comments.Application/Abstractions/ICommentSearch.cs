using Comments.Application.Comments;

namespace Comments.Application.Abstractions;

/// <summary>Full-text search over comments (Elasticsearch). The index is filled asynchronously from the outbox.</summary>
public interface ICommentSearch
{
    Task<SearchResult> SearchAsync(string query, int page, CancellationToken cancellationToken);
}

/// <summary>Writes comments into the search index. Indexing is idempotent: a document is keyed by the comment id.</summary>
public interface ICommentIndexer
{
    Task EnsureIndexAsync(CancellationToken cancellationToken);

    Task IndexAsync(IReadOnlyCollection<CommentDto> comments, CancellationToken cancellationToken);
}

public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, long Total, int Page, int PageSize);

/// <summary>A found comment. <see cref="Snippet"/> is HTML-encoded text with the matches wrapped in &lt;mark&gt;.</summary>
public sealed record SearchHit(long Id, long? RootId, string UserName, string Email, DateTimeOffset CreatedAt, string Snippet);
