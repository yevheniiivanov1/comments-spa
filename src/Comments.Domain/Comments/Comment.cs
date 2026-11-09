using Comments.Domain.Common;
using Comments.Domain.Markup;
using Comments.Domain.Users;

namespace Comments.Domain.Comments;

/// <summary>
/// A comment or a reply. Replies form a tree of any depth: <see cref="ParentId"/> is the comment answered,
/// <see cref="RootId"/> the top-level comment of the thread (null for top-level comments), so a whole thread is one index range.
/// <para>
/// Author name, e-mail and home page are stored as posted (a snapshot), not only through <see cref="User"/>:
/// the list of top-level comments is sorted by them, and with the values on this row each sort is served by its own index.
/// </para>
/// </summary>
public sealed class Comment : Entity
{
    private Comment()
    {
    }

    public long Id { get; private set; }

    public long? ParentId { get; private set; }

    public long? RootId { get; private set; }

    public int Depth { get; private set; }

    public int UserId { get; private set; }

    public User User { get; private set; } = null!;

    public string UserName { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public string? HomePage { get; private set; }

    /// <summary>Validated XHTML produced by <see cref="CommentMarkup"/>; safe to render as is.</summary>
    public string Html { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>For a top-level comment, the number of replies in its whole thread; 0 for replies.</summary>
    public int ReplyCount { get; private set; }

    public ClientInfo Client { get; private set; } = null!;

    public Attachment? Attachment { get; private set; }

    public bool IsTopLevel => ParentId is null;

    public static Comment Post(User author, Author snapshot, MarkupResult text, ClientInfo client, DateTimeOffset now) =>
        Create(author, snapshot, text, client, now, parent: null);

    public Comment Reply(User author, Author snapshot, MarkupResult text, ClientInfo client, DateTimeOffset now)
    {
        if (Id == 0)
        {
            throw new InvalidOperationException("Cannot reply to a comment that has not been saved.");
        }

        return Create(author, snapshot, text, client, now, parent: this);
    }

    public void Attach(Attachment attachment)
    {
        if (Attachment is not null)
        {
            throw new InvalidOperationException("A comment can have one attachment.");
        }

        Attachment = attachment;
    }

    private static Comment Create(User author, Author snapshot, MarkupResult text, ClientInfo client, DateTimeOffset now, Comment? parent)
    {
        if (!text.IsValid)
        {
            throw new ArgumentException("Comment text must be validated markup.", nameof(text));
        }

        var comment = new Comment
        {
            ParentId = parent?.Id,
            RootId = parent is null ? null : parent.RootId ?? parent.Id,
            Depth = parent is null ? 0 : parent.Depth + 1,
            User = author,
            UserName = snapshot.UserName.Value,
            Email = snapshot.Email.Value,
            HomePage = snapshot.HomePage?.Value,
            Html = text.Html!,
            CreatedAt = now,
            Client = client,
        };

        comment.Raise(new CommentPosted(comment, now));
        return comment;
    }
}

/// <summary>The author fields of the form, already validated.</summary>
public sealed record Author(UserName UserName, EmailAddress Email, HomePage? HomePage);

/// <summary>Raised when a comment is posted. Carries the entity, so its database id is read after the insert.</summary>
public sealed record CommentPosted(Comment Comment, DateTimeOffset OccurredAt) : IDomainEvent;
