using Comments.Application.Abstractions;
using Comments.Application.Comments;
using Comments.Domain.Markup;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;

namespace Comments.Infrastructure.Search;

/// <summary>
/// Comments in Elasticsearch for full-text search. The text is indexed three ways (standard, Russian and English
/// analyzers), so a word matches in any of its forms in either language.
/// <para>
/// Security: highlights are produced with the HTML encoder, so the comment text in a snippet is escaped and only the
/// &lt;mark&gt; tags added by Elasticsearch are markup.
/// </para>
/// </summary>
internal sealed class ElasticCommentSearch(ElasticsearchClient client, ILogger<ElasticCommentSearch> logger) : ICommentSearch, ICommentIndexer
{
    public const string IndexName = "comments";
    public const int PageSize = 20;
    private const int MaxQueryLength = 200;

    public async Task<SearchResult> SearchAsync(string query, int page, CancellationToken cancellationToken)
    {
        query = query.Trim();
        page = Math.Clamp(page, 1, 50);
        if (query.Length == 0)
        {
            return new SearchResult([], 0, page, PageSize);
        }

        var request = new SearchRequest<CommentDocument>(IndexName)
        {
            From = (page - 1) * PageSize,
            Size = PageSize,
            Query = new MultiMatchQuery
            {
                Query = query.Length > MaxQueryLength ? query[..MaxQueryLength] : query,
                Fields = Fields.FromStrings(["text", "text.ru", "text.en", "userName^2"]),
                Fuzziness = new Fuzziness("AUTO"),
                Operator = Operator.And,
            },
            Highlight = new Highlight
            {
                Encoder = HighlighterEncoder.Html,
                PreTags = ["<mark>"],
                PostTags = ["</mark>"],
                FragmentSize = 160,
                NumberOfFragments = 1,
                NoMatchSize = 160,
                Fields = new Dictionary<Field, HighlightField>
                {
                    ["text"] = new() { MatchedFields = Fields.FromStrings(["text", "text.ru", "text.en"]), Type = HighlighterType.FastVector },
                },
            },
            Sort =
            [
                new SortOptions { Score = new ScoreSort { Order = SortOrder.Desc } },
                new FieldSort("createdAt") { Order = SortOrder.Desc },
            ],
        };

        var response = await client.SearchAsync<CommentDocument>(request, cancellationToken);
        if (!response.IsValidResponse)
        {
            logger.LogError("Search failed: {Debug}", response.DebugInformation);
            throw new InvalidOperationException("Search is unavailable.");
        }

        var hits = response.Hits
            .Where(h => h.Source is not null)
            .Select(h => new SearchHit(
                h.Source!.Id,
                h.Source.RootId,
                h.Source.UserName,
                h.Source.Email,
                h.Source.CreatedAt,
                h.Highlight?.Values.SelectMany(v => v).FirstOrDefault() ?? System.Net.WebUtility.HtmlEncode(Shorten(h.Source.Text))))
            .ToList();

        return new SearchResult(hits, response.Total, page, PageSize);
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        var exists = await client.Indices.ExistsAsync(IndexName, cancellationToken);
        if (exists.Exists)
        {
            return;
        }

        var termVectors = TermVectorOption.WithPositionsOffsets;
        var response = await client.Indices.CreateAsync(new CreateIndexRequest(IndexName)
        {
            Mappings = new TypeMapping
            {
                Properties = new Properties
                {
                    { "id", new LongNumberProperty() },
                    { "rootId", new LongNumberProperty() },
                    { "parentId", new LongNumberProperty() },
                    { "userName", new TextProperty { Fields = new Properties { { "raw", new KeywordProperty() } } } },
                    { "email", new KeywordProperty() },
                    { "createdAt", new DateProperty() },
                    {
                        "text", new TextProperty
                        {
                            TermVector = termVectors,
                            Fields = new Properties
                            {
                                { "ru", new TextProperty { Analyzer = "russian", TermVector = termVectors } },
                                { "en", new TextProperty { Analyzer = "english", TermVector = termVectors } },
                            },
                        }
                    },
                },
            },
        }, cancellationToken);

        if (!response.IsValidResponse && response.ElasticsearchServerError?.Error.Type != "resource_already_exists_exception")
        {
            throw new InvalidOperationException($"Could not create the search index: {response.DebugInformation}");
        }
    }

    public async Task IndexAsync(IReadOnlyCollection<CommentDto> comments, CancellationToken cancellationToken)
    {
        if (comments.Count == 0)
        {
            return;
        }

        var response = await client.BulkAsync(b => b.Index(IndexName).IndexMany(comments.Select(CommentDocument.From)), cancellationToken);
        if (!response.IsValidResponse || response.Errors)
        {
            throw new InvalidOperationException($"Indexing failed: {response.DebugInformation}");
        }
    }

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..160] + "…";
}

/// <summary>What is stored in the index for one comment. The id doubles as the document id, so re-indexing is an overwrite.</summary>
internal sealed record CommentDocument(long Id, long? RootId, long? ParentId, string UserName, string Email, string Text, DateTimeOffset CreatedAt)
{
    public static CommentDocument From(CommentDto comment) => new(
        comment.Id,
        comment.RootId,
        comment.ParentId,
        comment.UserName,
        comment.Email,
        CommentMarkup.ToPlainText(comment.Html),
        comment.CreatedAt);
}
