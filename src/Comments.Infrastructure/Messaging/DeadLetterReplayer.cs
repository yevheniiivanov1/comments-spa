using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Comments.Infrastructure.Messaging;

/// <summary>
/// Moves every message from the dead-letter queue back to the queue it was dead-lettered from (read from the
/// <c>x-death</c> header), with a fresh retry count. Run it after fixing whatever made the messages fail.
/// </summary>
public sealed class DeadLetterReplayer(IConnection connection, ILogger<DeadLetterReplayer> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareTopologyAsync(channel, cancellationToken);

        var replayed = 0;
        while (await channel.BasicGetAsync(RabbitMqTopology.DeadLetterQueue, autoAck: false, cancellationToken) is { } message)
        {
            var target = SourceQueue(message.BasicProperties) ?? RabbitMqTopology.SearchIndexerQueue;
            var headers = new Dictionary<string, object?>(message.BasicProperties.Headers ?? new Dictionary<string, object?>());
            headers.Remove("x-death");
            headers.Remove(RabbitMqConsumer<object>.RetryAttemptHeader);

            var properties = new BasicProperties(message.BasicProperties) { Headers = headers, Expiration = null };
            await channel.BasicPublishAsync(string.Empty, target, mandatory: false, properties, message.Body, cancellationToken);
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken);
            replayed++;
        }

        logger.LogInformation("Replayed {Count} dead-lettered messages", replayed);
        return replayed;
    }

    private static string? SourceQueue(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers?.TryGetValue("x-death", out var value) != true || value is not IList<object> deaths || deaths.Count == 0)
        {
            return null;
        }

        return deaths[0] is IDictionary<string, object> death && death.TryGetValue("queue", out var queue)
            ? queue switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null,
            }
            : null;
    }
}
