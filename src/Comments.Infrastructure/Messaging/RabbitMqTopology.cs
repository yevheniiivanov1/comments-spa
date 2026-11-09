using RabbitMQ.Client;

namespace Comments.Infrastructure.Messaging;

/// <summary>
/// Broker layout. Integration events go to one durable topic exchange, keyed by event type.
/// <list type="bullet">
/// <item><c>comments.search-indexer</c>: quorum queue for the worker. A message that fails waits in
/// <c>comments.search-indexer.retry</c> (per-message TTL) and then returns to the work queue; after the last attempt it goes
/// to <c>comments.dead-letter</c>, from which the worker's <c>replay</c> command brings it back. The quorum delivery limit
/// only catches a consumer that crashes while handling a message.</item>
/// <item>One exclusive, auto-delete queue per API instance for live updates, so every instance pushes every event to its own WebSocket clients.</item>
/// </list>
/// </summary>
public static class RabbitMqTopology
{
    public const string EventsExchange = "comments.events";
    public const string DeadLetterExchange = "comments.dead-letter";
    public const string DeadLetterQueue = "comments.dead-letter";
    public const string SearchIndexerQueue = "comments.search-indexer";
    public const int DeliveryLimit = 5;

    /// <summary>The queue where a failed message of <paramref name="queue"/> waits before it is delivered again.</summary>
    public static string RetryQueue(string queue) => $"{queue}.retry";

    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: string.Empty, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Exchanges plus every durable work queue. The publisher declares them too: on a fresh broker an event published
    /// before its consumer first started would otherwise have no queue to land in and be dropped.
    /// </summary>
    public static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken) =>
        await DeclareWorkQueueAsync(channel, SearchIndexerQueue, Application.Comments.CommentPostedMessage.RoutingKey, cancellationToken);

    public static async Task<string> DeclareWorkQueueAsync(IChannel channel, string queue, string routingKey, CancellationToken cancellationToken)
    {
        await DeclareExchangesAsync(channel, cancellationToken);
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = DeliveryLimit,
                ["x-dead-letter-exchange"] = DeadLetterExchange,
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue, EventsExchange, routingKey, cancellationToken: cancellationToken);

        // Expired messages go back to the work queue through the default exchange.
        await channel.QueueDeclareAsync(
            RetryQueue(queue),
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = queue,
            },
            cancellationToken: cancellationToken);
        return queue;
    }

    public static async Task<string> DeclareInstanceQueueAsync(IChannel channel, string routingKey, CancellationToken cancellationToken)
    {
        await DeclareExchangesAsync(channel, cancellationToken);
        var declared = await channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(declared.QueueName, EventsExchange, routingKey, cancellationToken: cancellationToken);
        return declared.QueueName;
    }
}
