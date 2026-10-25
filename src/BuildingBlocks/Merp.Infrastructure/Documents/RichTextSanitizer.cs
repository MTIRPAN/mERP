using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using Ganss.Xss;
using Merp.Application.Abstractions.Documents;

namespace Merp.Infrastructure.Documents;

/// <summary>Allow-list HTML cleaning with HtmlSanitizer (D49).</summary>
internal sealed class RichTextSanitizer : IRichTextSanitizer
{
    // Their content is code or form controls, not text: dropped with the tag
    private static readonly HashSet<string> DroppedWithContent = new(["script", "style", "template", "noscript", "iframe", "object", "embed", "svg", "math", "textarea", "select", "button", "title"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Tags = ["p", "br", "strong", "b", "em", "i", "u", "s", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "a"];

    private readonly HtmlSanitizer _sanitizer;

    public RichTextSanitizer()
    {
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(["http", "https", "mailto"], StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<CssRuleType>(),
        });
        // The text of unknown tags (pasted spans, divs, headings 1) stays
        _sanitizer.KeepChildNodes = true;
        _sanitizer.RemovingTag += (_, e) =>
        {
            if (DroppedWithContent.Contains(e.Tag.LocalName))
            {
                while (e.Tag.FirstChild is { } child)
                {
                    e.Tag.RemoveChild(child);
                }
            }
        };
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement { LocalName: "a" } link && link.HasAttribute("href"))
            {
                link.SetAttribute("target", "_blank");
                link.SetAttribute("rel", "noopener noreferrer nofollow");
            }
        };
    }

    public string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var clean = _sanitizer.Sanitize(html).Trim();
        return IsEmpty(clean) ? null : clean;
    }

    /// <summary>Nothing but empty paragraphs and line breaks, as the editor leaves an emptied text.</summary>
    private static bool IsEmpty(string html)
    {
        var text = html
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("&nbsp;", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(text);
    }
}
