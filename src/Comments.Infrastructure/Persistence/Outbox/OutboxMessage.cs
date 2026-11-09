using System.Text.Json;
using Comments.Application.Comments;
using Comments.Domain.Comments;
using Comments.Domain.Common;
using Comments.Infrastructure.Json;

namespace Comments.Infrastructure.Persistence.Outbox;

/// <summary>An integration event waiting to be published to RabbitMQ (transactional outbox).</summary>
public sealed class OutboxMessage
{
    public const int MaxErrorLength = 2000;

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

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }
}
