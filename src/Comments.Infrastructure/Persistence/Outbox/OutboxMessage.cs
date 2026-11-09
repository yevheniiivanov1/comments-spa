using System.Text.Json;
using Comments.Application.Comments;
using Comments.Domain.Comments;
using Comments.Domain.Common;
using Comments.Infrastructure.Json;

namespace Comments.Infrastructure.Persistence.Outbox;

/// <summary>
/// An integration event waiting to be published to RabbitMQ (transactional outbox).
/// <para>
/// A message the broker rejects is retried with exponential backoff (2 s, 4 s, … up to 5 minutes) so it does not hold up
/// the messages behind it, and after <see cref="MaxAttempts"/> it is parked: kept for inspection, skipped by the
/// dispatcher, and released by the worker's <c>replay</c> command.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxErrorLength = 2000;
    public const int MaxAttempts = 20;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private OutboxMessage()
    {
    }

    public long Id { get; private set; }

    /// <summary>The routing key the message is published with.</summary>
    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Not before this time; set after a failed attempt.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Set when the message ran out of attempts.</summary>
    public DateTimeOffset? ParkedAt { get; private set; }

    public static OutboxMessage From(IDomainEvent domainEvent) => domainEvent switch
    {
        CommentPosted posted => new OutboxMessage
        {
            Type = CommentPostedMessage.RoutingKey,
            Payload = JsonSerializer.Serialize(new CommentPostedMessage(CommentDto.From(posted.Comment)), JsonDefaults.Options),
            OccurredAt = posted.OccurredAt,
        },
        _ => throw new NotSupportedException($"No integration event for {domainEvent.GetType().Name}."),
    };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        Attempts++;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        if (Attempts >= MaxAttempts)
        {
            ParkedAt = now;
            NextAttemptAt = null;
            return;
        }

        var backoff = TimeSpan.FromSeconds(Math.Pow(2, Attempts));
        NextAttemptAt = now + (backoff < MaxBackoff ? backoff : MaxBackoff);
    }
}
