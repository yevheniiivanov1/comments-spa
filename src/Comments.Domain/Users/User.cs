namespace Comments.Domain.Users;

/// <summary>
/// A person who posts comments, identified by e-mail. Name and home page follow the latest comment;
/// each comment also keeps the values it was posted with.
/// </summary>
public sealed class User
{
    private User()
    {
    }

    public int Id { get; private set; }

    public string UserName { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public string? HomePage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public static User Register(UserName userName, EmailAddress email, HomePage? homePage, DateTimeOffset now) => new()
    {
        UserName = userName.Value,
        Email = email.Value,
        HomePage = homePage?.Value,
        CreatedAt = now,
        LastSeenAt = now,
    };

    public void Touch(UserName userName, HomePage? homePage, DateTimeOffset now)
    {
        UserName = userName.Value;
        HomePage = homePage?.Value;
        LastSeenAt = now;
    }
}
