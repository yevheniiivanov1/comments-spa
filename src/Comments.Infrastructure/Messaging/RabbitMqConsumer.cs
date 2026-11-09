using System.Globalization;
using System.Text.Json;
using Comments.Infrastructure.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Comments.Infrastructure.Messaging;

/// <summary>
/// Base class for a background consumer of one JSON message type.
/// <para>
/// Messages are acknowledged after the handler succeeds. With a <see cref="Retry"/> policy a failed message is copied to
/// the queue's retry queue with a delay and acknowledged, so a dependency outage (Elasticsearch down) is waited out instead
/// of burning through redeliveries in milliseconds; after the last attempt it is dead-lettered. Without a policy a failure
/// is negatively acknowledged (requeued or dropped, see <see cref="RequeueOnFailure"/>). The broker connection is resolved
/// lazily and retried, so the host starts (and keeps serving HTTP) while RabbitMQ is still coming up or is restarting.
/// </para>
/// </summary>
public abstract class RabbitMqConsumer<TMessage>(IServiceProvider services, ILogger logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly TaskCompletionSource _consuming = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the consumer is subscribed to its queue for the first time.</summary>
    public Task Consuming => _consuming.Task;

    protected virtual ushort Prefetch => 20;

    public const string RetryAttemptHeader = "x-retry-attempt";

    protected virtual bool RequeueOnFailure => true;

    /// <summary>Delayed redelivery for work queues declared with <see cref="RabbitMqTopology.DeclareWorkQueueAsync"/>.</summary>
    protected virtual RetryPolicy? Retry => null;

    protected abstract Task<string> DeclareQueueAsync(IChannel channel, CancellationToken cancellationToken);

    protected abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken);

    /// <summary>Runs once before consuming starts (e.g. to create the search index).</summary>
    protected virtual Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PrepareAsync(stoppingToken);
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Consumer} stopped; reconnecting in {Delay}", GetType().Name, RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var connection = services.GetRequiredService<IConnection>();
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: Prefetch, global: false, stoppingToken);
        var queue = await DeclareQueueAsync(channel, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, JsonDefaults.Options)
                    ?? throw new InvalidOperationException("Empty message.");
                await HandleAsync(message, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                await HandleFailureAsync(channel, queue, delivery, ex, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("{Consumer} is consuming {Queue}", GetType().Name, queue);
        _consuming.TrySetResult();

        await closed.Task.WaitAsync(stoppingToken);
        throw new InvalidOperationException($"The channel of {queue} was closed.");
    }

    private async Task HandleFailureAsync(IChannel channel, string queue, BasicDeliverEventArgs delivery, Exception error, CancellationToken cancellationToken)
    {
        var messageId = delivery.BasicProperties.MessageId;
        if (Retry is not { } retry)
        {
            logger.LogError(error, "Failed to handle message {MessageId} from {Queue}", messageId, queue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: RequeueOnFailure, cancellationToken);
            return;
        }

        var attempt = RetryAttempt(delivery.BasicProperties) + 1;
        if (attempt > retry.MaxAttempts)
        {
            logger.LogError(error, "Message {MessageId} from {Queue} failed {Attempts} times; moving it to the dead-letter queue", messageId, queue, attempt);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        try
        {
            var headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [RetryAttemptHeader] = attempt,
            };
            var properties = new BasicProperties(delivery.BasicProperties)
            {
                Headers = headers,
                Expiration = ((long)retry.Delay.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            };
            await channel.BasicPublishAsync(string.Empty, RabbitMqTopology.RetryQueue(queue), mandatory: false, properties, delivery.Body, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            logger.LogWarning(error, "Message {MessageId} from {Queue} failed (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}",
                messageId, queue, attempt, retry.MaxAttempts, retry.Delay);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not schedule a retry for message {MessageId}; returning it to {Queue}", messageId, queue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }

    private static int RetryAttempt(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(RetryAttemptHeader, out var value) == true && value is not null
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : 0;
}

/// <summary>How long a failed message waits before the next attempt, and how many attempts it gets before it is dead-lettered.</summary>
public sealed record RetryPolicy(TimeSpan Delay, int MaxAttempts);
