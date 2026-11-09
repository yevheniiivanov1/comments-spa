using Comments.Domain.Markup;

namespace Comments.Domain.Tests;

public sealed class CommentMarkupTests
{
    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("<i>it</i> and <strong>bold</strong>", "<i>it</i> and <strong>bold</strong>")]
    [InlineData("<code>x = 1</code>", "<code>x = 1</code>")]
    [InlineData("<strong><i>both</i></strong>", "<strong><i>both</i></strong>")]
    [InlineData("a < b && c > d", "a &lt; b &amp;&amp; c &gt; d")]
    [InlineData("<code>if (a < b) return;</code>", "<code>if (a &lt; b) return;</code>")]
    [InlineData("&lt;script&gt; is text", "&lt;script&gt; is text")]
    [InlineData("Tom &amp; Jerry", "Tom &amp; Jerry")]
    [InlineData("line 1\r\nline 2", "line 1\nline 2")]
    public void Valid_markup_is_rebuilt_as_xhtml(string input, string expected)
    {
        var result = CommentMarkup.Parse(input);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(expected, result.Html);
    }

    [Fact]
    public void Link_keeps_only_href_and_title_and_gets_rel()
    {
        var result = CommentMarkup.Parse("""see <a title='Docs' href="https://example.com/?a=1&b=2">docs</a>""");

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(
            """see <a href="https://example.com/?a=1&amp;b=2" title="Docs" rel="nofollow ugc noopener">docs</a>""",
            result.Html);
        Assert.Equal("see docs", result.PlainText);
    }

    [Fact]
    public void Attribute_values_are_reencoded()
    {
        var result = CommentMarkup.Parse("""<a href="https://example.com" title="say &quot;hi&quot; > bye">x</a>""");

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("""title="say &quot;hi&quot; &gt; bye" """.TrimEnd(), result.Html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "Tag <script> is not allowed")]
    [InlineData("<img src=x onerror=alert(1)>", "is malformed")]
    [InlineData("<div>block</div>", "Tag <div> is not allowed")]
    [InlineData("<i>unclosed", "Tag <i> is not closed")]
    [InlineData("closing only</i>", "has no matching opening tag")]
    [InlineData("<i><strong>x</i></strong>", "not nested correctly")]
    [InlineData("<STRONG>x</STRONG>", "must be lowercase")]
    [InlineData("<i class=\"x\">x</i>", "cannot have attributes")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "must be a full http:// or https:// URL")]
    [InlineData("<a href=\"data:text/html,x\">x</a>", "must be a full http:// or https:// URL")]
    [InlineData("<a>x</a>", "needs an href")]
    [InlineData("<a href=\"https://a.com\" onclick=\"x()\">x</a>", "Attribute \"onclick\" is not allowed")]
    [InlineData("<a href=\"https://a.com\" href=\"https://b.com\">x</a>", "is repeated")]
    [InlineData("<a href=https://a.com>x</a>", "attribute values must be in quotes")]
    [InlineData("<a href=\"https://a.com\"><a href=\"https://b.com\">x</a></a>", "cannot be nested")]
    [InlineData("<i/>", "must have content")]
    [InlineData("<!-- hidden -->", "HTML comments are not allowed")]
    [InlineData("<![CDATA[x]]>", "is not allowed")]
    [InlineData("<i", "is not closed with '>'")]
    [InlineData("</i x=\"1\">", "cannot have attributes")]
    public void Invalid_markup_is_rejected_with_a_reason(string input, string expectedError)
    {
        var result = CommentMarkup.Parse(input);

        Assert.False(result.IsValid);
        Assert.Null(result.Html);
        Assert.Contains(result.Errors, e => e.Contains(expectedError, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    [InlineData("<i>  </i>")]
    public void Empty_text_is_required(string input)
    {
        var result = CommentMarkup.Parse(input);

        Assert.Equal(["Text is required."], result.Errors);
    }

    [Fact]
    public void Too_long_text_is_rejected()
    {
        var result = CommentMarkup.Parse(new string('a', CommentMarkup.MaxLength + 1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Control_characters_are_dropped()
    {
        var result = CommentMarkup.Parse("a\u0000b\u0007c");

        Assert.Equal("abc", result.Html);
    }

    [Theory]
    [InlineData("a&#1;b", "ab")]
    [InlineData("a&#x1F;b&#xFFFE;c", "abc")]
    [InlineData("a&#xD800;b", "a&amp;#xD800;b")]
    [InlineData("smile &#x1F600;", "smile \U0001F600")]
    public void Characters_xml_forbids_are_dropped_after_entities_are_decoded(string input, string expected)
    {
        var result = CommentMarkup.Parse(input);

        Assert.Equal(expected, result.Html);
        System.Xml.Linq.XDocument.Parse($"<div>{result.Html}</div>");
    }

    [Fact]
    public void Characters_xml_forbids_are_dropped_from_attributes()
    {
        var result = CommentMarkup.Parse("""<a href="https://example.com" title="a&#1;b">x</a>""");

        Assert.Contains("""title="ab" """.TrimEnd(), result.Html, StringComparison.Ordinal);
        System.Xml.Linq.XDocument.Parse($"<div>{result.Html}</div>");
    }

    [Fact]
    public void All_errors_are_reported_at_once()
    {
        var result = CommentMarkup.Parse("<div>x</div> <b>y</b> <i>z");

        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void Output_is_well_formed_xml()
    {
        var result = CommentMarkup.Parse("""<strong>A & B</strong> <a href="https://x.io/?q=1&r=2" title="t">l <i>i</i></a> <code>&lt;tag&gt;</code>""");

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var document = System.Xml.Linq.XDocument.Parse($"<div>{result.Html}</div>", System.Xml.Linq.LoadOptions.PreserveWhitespace);
        Assert.Equal("A & B l i <tag>", document.Root!.Value);
    }
}
