using System.Text.RegularExpressions;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

/// <summary>The contact details on a CV, found here so they never reach the AI provider (PLAN A3).</summary>
/// <param name="FirstName">Given names of the name on the first lines; null when none was found.</param>
/// <param name="LastName">The last word of that name.</param>
/// <param name="Email">The first e-mail address.</param>
/// <param name="Phone">The first phone number as written.</param>
/// <param name="LinkedInUrl">A LinkedIn profile link, with https.</param>
/// <param name="PortfolioUrl">The first other web link, with https.</param>
public sealed record CvContact(string? FirstName, string? LastName, string? Email, string? Phone, string? LinkedInUrl, string? PortfolioUrl);

/// <summary>Reads the contact details of a CV with regular expressions (PLAN A3).</summary>
internal static partial class CvContactExtractor
{
    public static CvContact Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var name = CandidateNameDetector.Detect(text);
        // The domain of an e-mail address is not a web link
        var links = UrlRegex().Matches(EmailRegex().Replace(text, " ")).Select(m => WithScheme(m.Value.TrimEnd('.', ',', ';', ')'))).ToList();
        var linkedIn = links.FirstOrDefault(l => l.Contains("linkedin.com/", StringComparison.OrdinalIgnoreCase));
        return new CvContact(
            name.Count >= 2 ? string.Join(' ', name.Take(name.Count - 1)) : null,
            name.Count >= 2 ? name[^1] : null,
            EmailRegex().Match(text) is { Success: true } email ? email.Value.ToLowerInvariant() : null,
            PhoneRegex().Match(text) is { Success: true } phone ? phone.Value.Trim() : null,
            linkedIn,
            links.FirstOrDefault(l => l != linkedIn));
    }

    private static string WithScheme(string link) =>
        link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? link
            : $"https://{link}";

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex EmailRegex();

    // Turkish mobile and landline numbers with or without +90/0, and international numbers with a + prefix
    [GeneratedRegex(@"(?<![\d+])(?:\+90[\s\-]?|0)?\(?5\d{2}\)?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}(?!\d)|(?<![\d+])(?:\+90[\s\-]?|0)\(?[2-4]\d{2}\)?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}(?!\d)|\+\d{1,3}[\s\-]?\(?\d{2,4}\)?(?:[\s\-]?\d{2,4}){2,4}(?!\d)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?:https?://)?(?:www\.)?(?:[A-Za-z0-9\-]+\.)+(?:com|net|org|io|dev|me|tr|co|app|info|design)(?:\.[a-z]{2})?(?:/[^\s<>""')]*)?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex UrlRegex();
}
