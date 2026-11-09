using Comments.Application.Abstractions;
using Comments.Application.Comments;
using Comments.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Comments.Infrastructure.Search;

public sealed class SearchIndexingOptions
{
    public const string Section = "SearchIndexing";

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>With the default delay, about an hour of Elasticsearch downtime before an event is dead-lettered.</summary>
    public int MaxAttempts { get; set; } = 240;
}

/// <summary>
/// Indexes every posted comment in Elasticsearch. Indexing by comment id makes redeliveries harmless. While Elasticsearch
/// is down, events wait in the retry queue and are indexed once it is back.
/// </summary>
internal sealed class SearchIndexerConsumer(
    IServiceProvider services,
    ICommentIndexer indexer,
    IOptions<SearchIndexingOptions> options,
    ILogger<SearchIndexerConsumer> logger)
    : RabbitMqConsumer<CommentPostedMessage>(services, logger)
{
    protected override RetryPolicy Retry => new(options.Value.RetryDelay, options.Value.MaxAttempts);

    protected override Task PrepareAsync(CancellationToken cancellationToken) => indexer.EnsureIndexAsync(cancellationToken);

    protected override Task<string> DeclareQueueAsync(IChannel channel, CancellationToken cancellationToken) =>
        RabbitMqTopology.DeclareWorkQueueAsync(channel, RabbitMqTopology.SearchIndexerQueue, CommentPostedMessage.RoutingKey, cancellationToken);

    protected override Task HandleAsync(CommentPostedMessage message, CancellationToken cancellationToken) =>
        indexer.IndexAsync([message.Comment], cancellationToken);
}
