using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Comments.Domain.Users;

namespace Comments.Domain.Markup;

/// <summary>
/// Validates comment text with the allowed tags (<c>&lt;a href title&gt;</c>, <c>&lt;code&gt;</c>, <c>&lt;i&gt;</c>, <c>&lt;strong&gt;</c>)
/// and rebuilds it as well-formed XHTML.
/// <para>
/// Security: the output never echoes the input. Text and attribute values are decoded and re-encoded and tags are
/// re-emitted from the parsed model, so nothing that is not explicitly allowed (other tags, attributes, event handlers,
/// <c>javascript:</c> links, comments, CDATA) can reach the page. Invalid input is rejected with errors, not silently "fixed",
/// so the author sees exactly what will be published.
/// </para>
/// <para>The Angular client mirrors these rules for instant feedback; the server result is authoritative.</para>
/// </summary>
public static partial class CommentMarkup
{
    public const int MaxLength = 10_000;
    public const int MaxTitleLength = 200;

    private const string AllowedTagList = "<a>, <code>, <i>, <strong>";

    private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal) { "a", "code", "i", "strong" };

    public static MarkupResult Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return MarkupResult.Invalid("Text is required.");
        }

        if (input.Length > MaxLength)
        {
            return MarkupResult.Invalid($"Text must be at most {MaxLength} characters.");
        }

        var parser = new Parser(Normalize(input));
        return parser.Run();
    }

    /// <summary>Unifies line breaks and drops control characters that XML 1.0 does not allow.</summary>
    private static string Normalize(string input)
    {
        var text = input.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return InvalidXmlCharacters().Replace(text, string.Empty);
    }

    internal static string Encode(string value, bool attribute)
    {
        var sb = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"' when attribute: sb.Append("&quot;"); break;
                case '\'' when attribute: sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// One tag at the current position: optional '/', a name, attributes with quoted values, optional '/' before '>'.
    /// Anchored with \G so it only matches exactly where the scanner found '&lt;'.
    /// </summary>
    [GeneratedRegex("""\G<(?<close>/)?(?<name>[A-Za-z][A-Za-z0-9]*)(?<attrs>(?:\s+[^\s=<>/"']+\s*=\s*(?:"[^"<]*"|'[^'<]*'))*)\s*(?<self>/)?>""", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex("""\s+(?<name>[^\s=<>/"']+)\s*=\s*(?:"(?<value>[^"<]*)"|'(?<value>[^'<]*)')""", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F￾￿]")]
    private static partial Regex InvalidXmlCharacters();

    private sealed class Parser(string input)
    {
        private readonly StringBuilder _html = new(input.Length + 32);
        private readonly StringBuilder _plain = new(input.Length);
        private readonly Stack<string> _open = new();
        private readonly List<string> _errors = [];

        public MarkupResult Run()
        {
            var textStart = 0;
            var position = 0;

            while ((position = input.IndexOf('<', position)) >= 0)
            {
                if (!LooksLikeTag(position))
                {
                    position++;
                    continue;
                }

                AppendText(input[textStart..position]);

                var match = Tag().Match(input, position);
                if (match.Success)
                {
                    HandleTag(match);
                    position += match.Length;
                }
                else
                {
                    var end = input.IndexOf('>', position);
                    end = end < 0 ? input.Length : end + 1;
                    _errors.Add(DescribeMalformed(input[position..end]));
                    position = end;
                }

                textStart = position;
            }

            AppendText(input[textStart..]);

            foreach (var name in _open)
            {
                _errors.Add($"Tag <{name}> is not closed.");
            }

            if (_errors.Count > 0)
            {
                return new MarkupResult(null, null, _errors.Distinct().ToArray());
            }

            var plain = _plain.ToString().Trim();
            return plain.Length == 0
                ? MarkupResult.Invalid("Text is required.")
                : new MarkupResult(_html.ToString().Trim(), plain, []);
        }

        /// <summary>'&lt;' starts markup only when followed by a letter, '/', '!' or '?'; "a &lt; b" stays text.</summary>
        private bool LooksLikeTag(int position) =>
            position + 1 < input.Length && input[position + 1] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '/' or '!' or '?';

        private void AppendText(string raw)
        {
            if (raw.Length == 0)
            {
                return;
            }

            var text = WebUtility.HtmlDecode(raw);
            _plain.Append(text);
            _html.Append(Encode(text, attribute: false));
        }

        private void HandleTag(Match match)
        {
            var source = match.Value;
            var name = match.Groups["name"].Value;
            var lower = name.ToLowerInvariant();
            var isClosing = match.Groups["close"].Success;

            if (!AllowedTags.Contains(lower))
            {
                _errors.Add($"Tag <{name}> is not allowed. Allowed tags: {AllowedTagList}.");
                return;
            }

            if (name != lower)
            {
                _errors.Add($"Tag names must be lowercase in XHTML: use <{lower}> instead of <{name}>.");
                return;
            }

            if (isClosing)
            {
                HandleClosing(name, source, match);
                return;
            }

            if (match.Groups["self"].Success)
            {
                _errors.Add($"Tag <{name}> must have content and a closing </{name}>, not \"{source}\".");
                return;
            }

            if (name == "a" && _open.Contains("a"))
            {
                _errors.Add("Links cannot be nested inside other links.");
                return;
            }

            var attributes = ParseAttributes(name, match.Groups["attrs"].Value);
            if (attributes is null)
            {
                return;
            }

            _open.Push(name);
            _html.Append('<').Append(name);
            foreach (var (attribute, value) in attributes)
            {
                _html.Append(' ').Append(attribute).Append("=\"").Append(Encode(value, attribute: true)).Append('"');
            }

            if (name == "a")
            {
                _html.Append(" rel=\"nofollow ugc noopener\"");
            }

            _html.Append('>');
        }

        private void HandleClosing(string name, string source, Match match)
        {
            if (match.Groups["attrs"].Length > 0 || match.Groups["self"].Success)
            {
                _errors.Add($"Closing tag \"{source}\" cannot have attributes.");
                return;
            }

            if (_open.Count == 0 || !_open.Contains(name))
            {
                _errors.Add($"Closing tag </{name}> has no matching opening tag.");
                return;
            }

            if (_open.Peek() != name)
            {
                _errors.Add($"Tags are not nested correctly: <{_open.Peek()}> must be closed before </{name}>.");
                return;
            }

            _open.Pop();
            _html.Append("</").Append(name).Append('>');
        }

        /// <summary>Returns the allowed attributes in a fixed order, or null after recording errors.</summary>
        private List<(string Name, string Value)>? ParseAttributes(string tag, string source)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var valid = true;

            foreach (Match attribute in Attribute().Matches(source))
            {
                var name = attribute.Groups["name"].Value;
                var value = WebUtility.HtmlDecode(attribute.Groups["value"].Value).Trim();

                if (tag != "a" || name is not ("href" or "title"))
                {
                    _errors.Add(tag == "a"
                        ? $"Attribute \"{name}\" is not allowed on <a>; only href and title are."
                        : $"Tag <{tag}> cannot have attributes.");
                    valid = false;
                    continue;
                }

                if (!values.TryAdd(name, value))
                {
                    _errors.Add($"Attribute \"{name}\" is repeated on <a>.");
                    valid = false;
                }
            }

            if (tag != "a")
            {
                return valid ? [] : null;
            }

            if (!values.TryGetValue("href", out var href) || href.Length == 0)
            {
                _errors.Add("Link <a> needs an href with the address.");
                valid = false;
            }
            else if (href.Length > HomePage.MaxLength || !HomePage.IsHttpUrl(href))
            {
                _errors.Add($"Link address \"{href}\" must be a full http:// or https:// URL.");
                valid = false;
            }

            if (values.TryGetValue("title", out var title) && title.Length > MaxTitleLength)
            {
                _errors.Add($"Link title must be at most {MaxTitleLength} characters.");
                valid = false;
            }

            if (!valid)
            {
                return null;
            }

            List<(string, string)> result = [("href", href!)];
            if (!string.IsNullOrEmpty(title))
            {
                result.Add(("title", title));
            }

            return result;
        }

        private static string DescribeMalformed(string fragment)
        {
            var shown = fragment.Length > 40 ? fragment[..40] + "…" : fragment;

            if (fragment.StartsWith("<!--", StringComparison.Ordinal))
            {
                return "HTML comments are not allowed.";
            }

            if (fragment.StartsWith("<!", StringComparison.Ordinal) || fragment.StartsWith("<?", StringComparison.Ordinal))
            {
                return $"\"{shown}\" is not allowed.";
            }

            return !fragment.EndsWith('>')
                ? $"Tag \"{shown}\" is not closed with '>'. To show a literal '<', write &lt;."
                : $"Tag \"{shown}\" is malformed: attribute values must be in quotes, e.g. <a href=\"https://example.com\">.";
        }
    }
}

/// <summary>Result of <see cref="CommentMarkup.Parse"/>: either safe XHTML with its plain text, or the errors to show the author.</summary>
public sealed record MarkupResult(string? Html, string? PlainText, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static MarkupResult Invalid(string error) => new(null, null, [error]);
}
