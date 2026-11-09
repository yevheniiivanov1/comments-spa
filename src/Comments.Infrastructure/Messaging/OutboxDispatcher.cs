using System.Globalization;
using System.Text;
using Comments.Infrastructure.Persistence;
using Comments.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Comments.Infrastructure.Messaging;

/// <summary>
/// Publishes outbox rows to RabbitMQ (transactional outbox, at-least-once).
/// <para>
/// Each batch is claimed with <c>UPDLOCK, READPAST</c> inside a transaction, so several worker instances split the work
/// without publishing the same row twice concurrently. A row is marked processed only after the broker confirmed the
/// message (publisher confirms). If the process dies between the confirm and the commit, the row is published again
/// on the next run; consumers are idempotent for that reason, and they do not depend on order.
/// </para>
/// <para>
/// A message the open channel fails to publish is marked failed with backoff and the batch goes on, so one bad message
/// cannot block the rest. A closed channel means the broker is gone: the batch stops without counting attempts, and the
/// loop reconnects.
/// </para>
/// </summary>
internal sealed class OutboxDispatcher(
    IServiceProvider services,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IChannel? channel = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (channel is not { IsOpen: true })
                    {
                        channel = await OpenChannelAsync(stoppingToken);
                    }

                    var published = await DispatchBatchAsync(channel, stoppingToken);
                    if (published < BatchSize)
                    {
                        await Task.Delay(IdleDelay, time, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Outbox dispatch failed; retrying in {Delay}", ErrorDelay);
                    await Task.Delay(ErrorDelay, time, stoppingToken);
                }
            }
        }
        finally
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }
    }

    private async Task<IChannel> OpenChannelAsync(CancellationToken cancellationToken)
    {
        var connection = services.GetRequiredService<IConnection>();
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareTopologyAsync(channel, cancellationToken);
        return channel;
    }

    private async Task<int> DispatchBatchAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommentsDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var now = time.GetUtcNow();
            var batch = await db.OutboxMessages
                .FromSql($"""
                    SELECT TOP ({BatchSize}) * FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE [ProcessedAt] IS NULL AND [ParkedAt] IS NULL AND ([NextAttemptAt] IS NULL OR [NextAttemptAt] <= {now})
                    ORDER BY [Id]
                    """)
                .ToListAsync(ct);

            var published = 0;
            foreach (var message in batch)
            {
                try
                {
                    await PublishAsync(channel, message, ct);
                    message.MarkProcessed(time.GetUtcNow());
                    published++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && channel.IsOpen)
                {
                    message.MarkFailed(ex.Message, time.GetUtcNow());
                    if (message.ParkedAt is not null)
                    {
                        logger.LogError(ex, "Outbox message {Id} ({Type}) failed {Attempts} times and was parked", message.Id, message.Type, message.Attempts);
                    }
                    else
                    {
                        logger.LogWarning(ex, "Could not publish outbox message {Id} ({Type}); next attempt at {NextAttemptAt}", message.Id, message.Type, message.NextAttemptAt);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "The broker channel closed while publishing; reconnecting");
                    break;
                }
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            if (published > 0)
            {
                logger.LogDebug("Published {Count} outbox messages", published);
            }

            return published;
        }, cancellationToken);
    }

    private static async Task PublishAsync(IChannel channel, OutboxMessage message, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(CultureInfo.InvariantCulture),
            Type = message.Type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
        };

        await channel.BasicPublishAsync(
            RabbitMqTopology.EventsExchange,
            routingKey: message.Type,
            mandatory: false,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(message.Payload),
            cancellationToken: cancellationToken);
    }
}
