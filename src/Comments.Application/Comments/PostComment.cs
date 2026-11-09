using Comments.Application.Abstractions;
using Comments.Domain.Comments;
using Comments.Domain.Markup;
using Comments.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Comments.Application.Comments;

public sealed record PostCommentCommand(
    string? UserName,
    string? Email,
    string? HomePage,
    string? Text,
    long? ParentId,
    string? CaptchaId,
    string? CaptchaAnswer,
    FileUpload? File,
    ClientInfo Client);

public sealed record PostCommentResult(CommentDto? Comment, IReadOnlyDictionary<string, string[]>? Errors)
{
    public bool Succeeded => Comment is not null;
}

/// <summary>
/// Posts a comment or a reply.
/// <para>
/// Order matters: everything is validated first (the CAPTCHA is consumed on every attempt), then the attachment is stored,
/// then the comment, the author and the outbox message are written in one transaction, and only after the commit the cached
/// list and thread are invalidated. If the database write fails, the stored file is deleted again.
/// </para>
/// </summary>
public sealed class PostCommentHandler(
    ICommentsDbContext db,
    ICaptchaService captcha,
    IAttachmentProcessor attachments,
    IFileStorage storage,
    ICommentCache cache,
    TimeProvider time,
    ILogger<PostCommentHandler> logger)
{
    public async Task<PostCommentResult> HandleAsync(PostCommentCommand command, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.Add("userName", UserName.Validate(command.UserName));
        errors.Add("email", EmailAddress.Validate(command.Email?.Trim()));
        errors.Add("homePage", HomePage.Validate(command.HomePage));

        var markup = CommentMarkup.Parse(command.Text);
        errors.Add("text", markup.Errors);

        if (!await captcha.VerifyAsync(command.CaptchaId, command.CaptchaAnswer, cancellationToken))
        {
            errors.Add("captcha", "The characters do not match the picture. Please enter the new one.");
        }

        Comment? parent = null;
        if (command.ParentId is { } parentId)
        {
            parent = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId, cancellationToken);
            errors.Add("parentId", parent is null ? "The comment you are replying to does not exist." : null);
        }

        ProcessedFile? file = null;
        if (command.File is not null && errors.IsEmpty)
        {
            var processed = await attachments.ProcessAsync(command.File, cancellationToken);
            errors.Add("file", processed.Error);
            file = processed.File;
        }

        if (!errors.IsEmpty)
        {
            return new PostCommentResult(null, errors.ToDictionary());
        }

        var author = new Author(
            UserName.Create(command.UserName!),
            EmailAddress.Create(command.Email!.Trim()),
            HomePage.CreateOptional(command.HomePage));

        var storageKey = file is null ? null : await storage.SaveAsync(file, cancellationToken);
        try
        {
            var comment = await SaveAsync(author, markup, parent, file, storageKey, command.Client, cancellationToken);

            await cache.InvalidateAsync(CacheRegion.TopLevel, cancellationToken);
            if (comment.RootId is { } rootId)
            {
                await cache.InvalidateAsync(CacheRegion.Thread(rootId), cancellationToken);
            }

            return new PostCommentResult(CommentDto.From(comment), null);
        }
        catch when (storageKey is not null)
        {
            await DeleteOrphanAsync(storageKey);
            throw;
        }
    }

    /// <summary>
    /// The first comment of a new e-mail can race with another one from the same address; the loser of the unique index
    /// retries once and then finds the user. Transient SQL errors are retried by the execution strategy, which replays the whole transaction.
    /// </summary>
    private async Task<Comment> SaveAsync(
        Author author, MarkupResult markup, Comment? parent, ProcessedFile? file, string? storageKey, ClientInfo client, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(
                    ct => SaveOnceAsync(author, markup, parent, file, storageKey, client, ct),
                    cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt == 1 && db.IsUniqueConstraintViolation(ex))
            {
                logger.LogInformation("User {Email} was registered concurrently; retrying", author.Email.Value);
            }
        }
    }

    private async Task<Comment> SaveOnceAsync(
        Author author, MarkupResult markup, Comment? parent, ProcessedFile? file, string? storageKey, ClientInfo client, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = TruncateToMilliseconds(time.GetUtcNow());
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == author.Email.Value, cancellationToken);
        if (user is null)
        {
            user = User.Register(author.UserName, author.Email, author.HomePage, now);
            db.Users.Add(user);
        }
        else
        {
            user.Touch(author.UserName, author.HomePage, now);
        }

        var comment = parent is null
            ? Comment.Post(user, author, markup, client, now)
            : parent.Reply(user, author, markup, client, now);

        if (file is not null)
        {
            comment.Attach(file.ToAttachment(storageKey!));
        }

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        if (comment.RootId is { } rootId)
        {
            await db.Comments
                .Where(c => c.Id == rootId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReplyCount, c => c.ReplyCount + 1), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return comment;
    }

    /// <summary>The column keeps milliseconds; the response must show the same instant a later read returns.</summary>
    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Offset);

    private async Task DeleteOrphanAsync(string storageKey)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete orphaned attachment {Key}", storageKey);
        }
    }
}
