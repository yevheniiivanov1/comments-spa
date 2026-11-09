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
/// Messages are acknowledged after the handler succeeds. A failure is negatively acknowledged: with requeue on work
/// queues, where the quorum queue's delivery limit dead-letters a message that keeps failing; without requeue for
/// best-effort consumers. The broker connection is resolved lazily and retried, so the host starts (and keeps serving
/// HTTP) while RabbitMQ is still coming up or is restarting.
/// </para>
/// </summary>
public abstract class RabbitMqConsumer<TMessage>(IServiceProvider services, ILogger logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly TaskCompletionSource _consuming = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the consumer is subscribed to its queue for the first time.</summary>
    public Task Consuming => _consuming.Task;

    protected virtual ushort Prefetch => 20;

    protected virtual bool RequeueOnFailure => true;

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
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
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
                logger.LogError(ex, "Failed to handle message {MessageId} from {Queue}", delivery.BasicProperties.MessageId, queue);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: RequeueOnFailure, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("{Consumer} is consuming {Queue}", GetType().Name, queue);
        _consuming.TrySetResult();

        await closed.Task.WaitAsync(stoppingToken);
        throw new InvalidOperationException($"The channel of {queue} was closed.");
    }
}
