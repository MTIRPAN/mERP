using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Merp.Modules.Recruitment.Application.Openings;

/// <summary>What an exclusionary phrase of an opening is about (D57).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LanguageWarningCategory>))]
public enum LanguageWarningCategory
{
    Age,
    Gender,
    MaritalStatus,
    MilitaryService,
    Nationality,
    Religion,
    Health,
    Appearance,
    Other,
}

/// <summary>Which text of an opening a warning is about; Brief is the short description given for an AI draft.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OpeningTextField>))]
public enum OpeningTextField
{
    Title,
    Description,
    Qualifications,
    Brief,
}

/// <summary>Where a warning comes from: the fixed rule list or the AI review.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LanguageWarningSource>))]
public enum LanguageWarningSource
{
    Rule,
    Ai,
}

/// <summary>A phrase of an opening that may exclude candidates for something unrelated to the job; a warning, never a block (D57).</summary>
/// <param name="Field">The text the phrase is in.</param>
/// <param name="Phrase">As written in the text.</param>
/// <param name="Category">What the phrase is about.</param>
/// <param name="Suggestion">A neutral alternative from the AI review; the rule list leaves it to the category's hint.</param>
/// <param name="Source">The rule list or the AI review.</param>
public sealed record OpeningLanguageWarning(
    OpeningTextField Field,
    string Phrase,
    LanguageWarningCategory Category,
    string? Suggestion,
    LanguageWarningSource Source);

/// <summary>
/// The fixed list of exclusionary phrases (D57) in Turkish and English: age limits, gender, marital status, military
/// service, nationality, religion, health and appearance. It runs on every read of an opening, needs no AI and only
/// warns; the AI review finds what a list cannot.
/// </summary>
public static partial class InclusiveLanguage
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Patterns run on the text lowered in Turkish with ı folded to i (İ/I/ı/i are the same letter), so they are
    // written in lower case without ı. Lowering keeps the length, so a match is the phrase of the original text.
    private static readonly (Regex Pattern, LanguageWarningCategory Category)[] Rules =
    [
        (AgeRange(), LanguageWarningCategory.Age),
        (AgeLimit(), LanguageWarningCategory.Age),
        (AgeWords(), LanguageWarningCategory.Age),
        (GenderWords(), LanguageWarningCategory.Gender),
        (MaritalWords(), LanguageWarningCategory.MaritalStatus),
        (MilitaryWords(), LanguageWarningCategory.MilitaryService),
        (NationalityWords(), LanguageWarningCategory.Nationality),
        (ReligionWords(), LanguageWarningCategory.Religion),
        (HealthWords(), LanguageWarningCategory.Health),
        (AppearanceWords(), LanguageWarningCategory.Appearance),
    ];

    /// <summary>The warnings of an opening's title and texts (HTML), one per phrase and field.</summary>
    public static IReadOnlyList<OpeningLanguageWarning> Check(string? title, string? description, string? qualifications) =>
    [
        .. Check(OpeningTextField.Title, title),
        .. Check(OpeningTextField.Description, PlainText(description)),
        .. Check(OpeningTextField.Qualifications, PlainText(qualifications)),
    ];

    /// <summary>The warnings of one plain text.</summary>
    public static IEnumerable<OpeningLanguageWarning> Check(OpeningTextField field, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var folded = text.ToLower(Turkish).Replace('ı', 'i');
        var found = new List<OpeningLanguageWarning>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pattern, category) in Rules)
        {
            foreach (Match match in pattern.Matches(folded))
            {
                if (seen.Add(match.Value))
                {
                    found.Add(new OpeningLanguageWarning(field, text.Substring(match.Index, match.Length).Trim(), category, null, LanguageWarningSource.Rule));
                }
            }
        }

        return found;
    }

    /// <summary>The text of sanitized HTML: tags become spaces, entities are decoded.</summary>
    public static string? PlainText(string? html) =>
        string.IsNullOrWhiteSpace(html) ? null : Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\b\d{2}\s*[-–/]\s*\d{2}\s*(yaş\w*|years?\s+old|years?\s+of\s+age)", RegexOptions.CultureInvariant)]
    private static partial Regex AgeRange();

    [GeneratedRegex(@"(en\s+(fazla|az|çok)\s+\d{2}\s*yaş\w*|\d{2}\s*yaş\w*\s*(alt\w*|üst\w*|üzeri\w*|geçmemiş|aşmamış|büyük|küçük)|\b(under|over|below|above|aged|max(imum)?|min(imum)?)\s+(age\s+)?\d{2}\b|\bage\s+(limit|range)\b|yaş\s+(sinir\w*|araliğ\w*))", RegexOptions.CultureInvariant)]
    private static partial Regex AgeLimit();

    [GeneratedRegex(@"\b(genç(\s+ve)?\s+dinamik|genç\s+(aday\w*|eleman\w*|personel\w*)|\byoung\b|digital\s+natives?|recent\s+graduates?\s+only)\b", RegexOptions.CultureInvariant)]
    private static partial Regex AgeWords();

    [GeneratedRegex(@"\b(erkek|bayan|kadin)(\s+(aday\w*|eleman\w*|personel\w*|çalişan\w*))?\b|\b(male|female)(\s+(candidates?|applicants?))?\b|\b(salesman|chairman|manpower)\b", RegexOptions.CultureInvariant)]
    private static partial Regex GenderWords();

    [GeneratedRegex(@"\b(evli|bekar|bekâr|medeni\s+hal\w*|married|unmarried|marital\s+status)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MaritalWords();

    [GeneratedRegex(@"\b(askerli(k|ğ)\w*|military\s+service)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MilitaryWords();

    [GeneratedRegex(@"\b(t\.?\s?c\.?\s+vatandaş\w*|türk\s+vatandaş\w*|türk\s+uyruklu|native\s+(english|turkish|german)\s+speakers?|ana\s?dili\s+(türkçe|ingilizce)\s+olan)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NationalityWords();

    [GeneratedRegex(@"\b(namaz\w*|dindar|oruç\w*|religious|christian|muslim)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ReligionWords();

    [GeneratedRegex(@"\b(sağlik\s+(sorunu|problemi)\s+olmayan|engel\w*\s+olmayan|engelli\s+olmayan|sağlikli\s+(aday\w*|bireyler)|able[- ]bodied|no\s+disabilit\w+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex HealthWords();

    [GeneratedRegex(@"\b((hoş|düzgün|güzel|bakimli|temiz)\s+(görünümlü|görünüşlü)|diş\s+görünüş\w*|boy\w*\s+(en\s+az\s+)?\d{3}|kilo\w*\s+\d{2}|attractive|good[- ]looking|well[- ]presented)\b", RegexOptions.CultureInvariant)]
    private static partial Regex AppearanceWords();
}

// Output of the model for the AI review and the issues of a draft. The JSON schema sent to the provider is generated
// from these types, so the descriptions are part of the prompt: change them with OpeningTextPrompt.Version.

[Description("A phrase that may exclude candidates for something unrelated to the job.")]
public sealed record OpeningTextIssue(
    [property: Description("The text the phrase is in.")] OpeningTextField Field,
    [property: Description("The phrase exactly as written in that text.")] string Phrase,
    LanguageWarningCategory Category,
    [property: Description("A neutral, job-related way to say it, or how to leave it out, in the output language.")] string Suggestion);
