using Comments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comments.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    /// <summary>How long published messages are kept for troubleshooting before they are deleted.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Deletes published outbox rows older than the retention period, in batches so a large backlog never takes one long
/// lock. Each row carries the whole comment, so without this the table would grow by ~50k rows of HTML a day.
/// </summary>
public sealed class OutboxCleaner(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, TimeProvider time, ILogger<OutboxCleaner> logger)
    : BackgroundService
{
    private const int BatchSize = 5_000;

    public async Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommentsDbContext>();
        var cutoff = time.GetUtcNow() - options.Value.Retention;

        var total = 0;
        int deleted;
        do
        {
            deleted = await db.OutboxMessages
                .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        if (total > 0)
        {
            logger.LogInformation("Deleted {Count} published outbox messages older than {Cutoff}", total, cutoff);
        }

        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox cleanup failed");
            }

            await Task.Delay(options.Value.CleanupInterval, time, stoppingToken);
        }
    }
}

/// <summary>Releases parked outbox messages so the dispatcher tries them again (the worker's <c>replay</c> command).</summary>
public sealed class OutboxParking(CommentsDbContext db)
{
    public Task<int> UnparkAllAsync(CancellationToken cancellationToken) =>
        db.OutboxMessages
            .Where(m => m.ParkedAt != null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ParkedAt, (DateTimeOffset?)null)
                    .SetProperty(m => m.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(m => m.Attempts, 0),
                cancellationToken);
}
