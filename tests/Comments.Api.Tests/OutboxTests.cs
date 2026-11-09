using Comments.Domain.Comments;
using Comments.Domain.Markup;
using Comments.Domain.Users;
using Comments.Infrastructure.Messaging;
using Comments.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Comments.Api.Tests;

public sealed class OutboxTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Failed_message_backs_off_exponentially_and_is_parked_after_the_last_attempt()
    {
        var message = NewMessage();

        message.MarkFailed("nack", Now);
        Assert.Equal(Now.AddSeconds(2), message.NextAttemptAt);
        message.MarkFailed("nack", Now);
        Assert.Equal(Now.AddSeconds(4), message.NextAttemptAt);

        for (var i = 2; i < 10; i++)
        {
            message.MarkFailed("nack", Now);
        }

        Assert.Equal(Now.AddMinutes(5), message.NextAttemptAt);
        Assert.Null(message.ParkedAt);

        while (message.Attempts < OutboxMessage.MaxAttempts)
        {
            message.MarkFailed("nack", Now);
        }

        Assert.Equal(Now, message.ParkedAt);
        Assert.Null(message.NextAttemptAt);
    }

    [Fact]
    public async Task Cleanup_deletes_published_messages_past_the_retention_only()
    {
        var old = NewMessage();
        old.MarkProcessed(DateTimeOffset.UtcNow.AddDays(-30));
        var recent = NewMessage();
        recent.MarkProcessed(DateTimeOffset.UtcNow.AddHours(-1));
        var parked = NewMessage();
        while (parked.ParkedAt is null)
        {
            parked.MarkFailed("nack", DateTimeOffset.UtcNow.AddDays(-30));
        }

        await factory.WithDbAsync(async db =>
        {
            db.OutboxMessages.AddRange(old, recent, parked);
            return await db.SaveChangesAsync(Ct);
        });

        await factory.Services.GetRequiredService<OutboxCleaner>().CleanAsync(Ct);

        var left = await factory.WithDbAsync(db => db.OutboxMessages
            .Where(m => m.Id == old.Id || m.Id == recent.Id || m.Id == parked.Id)
            .Select(m => m.Id)
            .ToListAsync(Ct));
        Assert.Equal([recent.Id, parked.Id], left.Order());
    }

    private static OutboxMessage NewMessage()
    {
        var author = new Author(UserName.Create("Outbox"), EmailAddress.Create("outbox-test@example.com"), null);
        var user = User.Register(author.UserName, author.Email, null, Now);
        var comment = Comment.Post(user, author, CommentMarkup.Parse("outbox"), ClientInfo.Create("127.0.0.1", null), Now);
        return OutboxMessage.From(Assert.Single(comment.DomainEvents));
    }
}
