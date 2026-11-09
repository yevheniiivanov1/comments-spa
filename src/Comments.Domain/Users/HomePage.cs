namespace Comments.Domain.Users;

/// <summary>An absolute http(s) URL. Other schemes (javascript:, data:, …) are rejected because the link is rendered on the page.</summary>
public sealed record HomePage
{
    public const int MaxLength = 2048;

    private HomePage(string value) => Value = value;

    public string Value { get; }

    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > MaxLength)
        {
            return $"Home page must be at most {MaxLength} characters.";
        }

        return IsHttpUrl(value) ? null : "Home page must be a full http:// or https:// address.";
    }

    public static HomePage? CreateOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Validate(value) is { } error ? throw new ArgumentException(error, nameof(value)) : new HomePage(value.Trim());
    }

    public static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);

    public override string ToString() => Value;
}
