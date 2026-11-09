using System.Text.RegularExpressions;

namespace Comments.Domain.Users;

/// <summary>A user name: Latin letters and digits only.</summary>
public sealed partial record UserName
{
    public const int MaxLength = 50;

    private UserName(string value) => Value = value;

    public string Value { get; }

    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "User Name is required.";
        }

        if (value.Length > MaxLength)
        {
            return $"User Name must be at most {MaxLength} characters.";
        }

        return AllowedCharacters().IsMatch(value) ? null : "User Name may contain only Latin letters and digits.";
    }

    public static UserName Create(string value) =>
        Validate(value) is { } error ? throw new ArgumentException(error, nameof(value)) : new UserName(value);

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedCharacters();
}
