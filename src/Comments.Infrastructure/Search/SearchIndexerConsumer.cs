using Comments.Application.Abstractions;
using Comments.Application.Comments;
using Comments.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Comments.Infrastructure.Search;

/// <summary>Indexes every posted comment in Elasticsearch. Indexing by comment id makes redeliveries harmless.</summary>
internal sealed class SearchIndexerConsumer(IServiceProvider services, ICommentIndexer indexer, ILogger<SearchIndexerConsumer> logger)
    : RabbitMqConsumer<CommentPostedMessage>(services, logger)
{
    protected override Task PrepareAsync(CancellationToken cancellationToken) => indexer.EnsureIndexAsync(cancellationToken);

    protected override Task<string> DeclareQueueAsync(IChannel channel, CancellationToken cancellationToken) =>
        RabbitMqTopology.DeclareWorkQueueAsync(channel, RabbitMqTopology.SearchIndexerQueue, CommentPostedMessage.RoutingKey, cancellationToken);

    protected override Task HandleAsync(CommentPostedMessage message, CancellationToken cancellationToken) =>
        indexer.IndexAsync([message.Comment], cancellationToken);
}
