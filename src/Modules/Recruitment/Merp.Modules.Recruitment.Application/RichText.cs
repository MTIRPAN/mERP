using System.Net;
using System.Text.RegularExpressions;

namespace Merp.Modules.Recruitment.Application;

/// <summary>Plain text of the sanitized HTML of openings (D49), e.g. the requirements a CV is matched against (A3).</summary>
internal static partial class RichText
{
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = BreakRegex().Replace(html, "\n");
        text = ItemRegex().Replace(text, "- ");
        text = WebUtility.HtmlDecode(TagRegex().Replace(text, string.Empty));
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != "-");
        return string.Join('\n', lines) is { Length: > 0 } plain ? plain : null;
    }

    [GeneratedRegex(@"<br\s*/?>|</(?:p|li|h[2-4]|blockquote)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ItemRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TagRegex();
}
