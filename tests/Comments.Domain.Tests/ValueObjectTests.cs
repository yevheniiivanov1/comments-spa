using Comments.Domain.Users;

namespace Comments.Domain.Tests;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("Anonym", null)]
    [InlineData("Rum8", null)]
    [InlineData("", "User Name is required.")]
    [InlineData("John Doe", "User Name may contain only Latin letters and digits.")]
    [InlineData("José", "User Name may contain only Latin letters and digits.")]
    [InlineData("john_doe", "User Name may contain only Latin letters and digits.")]
    public void UserName_allows_latin_letters_and_digits(string value, string? error) =>
        Assert.Equal(error, UserName.Validate(value));

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("first.last+tag@sub.example.co", true)]
    [InlineData("user@localhost", false)]
    [InlineData("user@@example.com", false)]
    [InlineData("user example.com", false)]
    [InlineData("", false)]
    public void Email_must_be_an_address(string value, bool valid) =>
        Assert.Equal(valid, EmailAddress.Validate(value) is null);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/path?q=1", true)]
    [InlineData("example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ftp://example.com", false)]
    public void HomePage_is_optional_http_url(string? value, bool valid) =>
        Assert.Equal(valid, HomePage.Validate(value) is null);
}
