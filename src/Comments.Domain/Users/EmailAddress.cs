using System.Text.RegularExpressions;

namespace Comments.Domain.Users;

/// <summary>An e-mail address in the usual <c>local@domain.tld</c> form.</summary>
public sealed partial record EmailAddress
{
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "E-mail is required.";
        }

        if (value.Length > MaxLength)
        {
            return $"E-mail must be at most {MaxLength} characters.";
        }

        return Format().IsMatch(value) ? null : "E-mail is not a valid address.";
    }

    public static EmailAddress Create(string value) =>
        Validate(value) is { } error ? throw new ArgumentException(error, nameof(value)) : new EmailAddress(value);

    public override string ToString() => Value;

    [GeneratedRegex(@"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\.[A-Za-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
