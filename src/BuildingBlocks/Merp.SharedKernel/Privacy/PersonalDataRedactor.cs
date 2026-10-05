using System.Text;
using System.Text.RegularExpressions;
using Merp.SharedKernel.Text;
using Merp.SharedKernel.ValueObjects;

namespace Merp.SharedKernel.Privacy;

/// <summary>Kind of personal data removed by <see cref="PersonalDataRedactor"/>.</summary>
public enum RedactionKind
{
    Email,
    Phone,
    NationalId,
    Iban,
    Url,
    Name,

    /// <summary>
    /// Value of a labelled field that must not leave the system or influence an evaluation: birth date, age, gender,
    /// marital status, nationality, religion, health, disability, blood type, military service, address, etc.
    /// </summary>
    SensitiveField,
}

/// <summary>Redacted text and how many values of each kind were removed.</summary>
public sealed record RedactionResult(string Text, IReadOnlyDictionary<RedactionKind, int> Counts)
{
    public int Total => Counts.Values.Sum();
}

/// <summary>
/// Removes direct identifiers and special category personal data (KVKK md. 6) from free text before it is sent to an
/// external service such as an AI provider (D24). Best effort: it recognises e-mail addresses, phone numbers, T.C.
/// identity numbers, IBANs, profile links, the given names, and the values of labelled fields such as
/// <c>Doğum Tarihi: …</c> or <c>Marital status: …</c>. Free text without labels is not understood.
/// </summary>
public static partial class PersonalDataRedactor
{
    public const string EmailPlaceholder = "[EMAIL]";
    public const string PhonePlaceholder = "[PHONE]";
    public const string NationalIdPlaceholder = "[NATIONAL_ID]";
    public const string IbanPlaceholder = "[IBAN]";
    public const string UrlPlaceholder = "[URL]";
    public const string NamePlaceholder = "[NAME]";
    public const string SensitivePlaceholder = "[REDACTED]";

    private const int MinNameLength = 2;

    public static RedactionResult Redact(string text, IEnumerable<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var counts = new Dictionary<RedactionKind, int>();

        // Order matters: e-mail addresses contain domains, IBANs and identity numbers contain digit runs.
        text = Replace(text, EmailRegex(), RedactionKind.Email, counts, _ => EmailPlaceholder);
        text = Replace(text, UrlRegex(), RedactionKind.Url, counts, _ => UrlPlaceholder);
        text = Replace(text, IbanRegex(), RedactionKind.Iban, counts, m => Iban.Create(m.Value).IsSuccess ? IbanPlaceholder : null);
        text = Replace(text, ElevenDigitsRegex(), RedactionKind.NationalId, counts, m => Tckn.IsValid(m.Value) ? NationalIdPlaceholder : null);
        text = Replace(text, PhoneRegex(), RedactionKind.Phone, counts, _ => PhonePlaceholder);
        text = ReplaceFolded(text, SensitiveFieldRegex(), "value", RedactionKind.SensitiveField, counts, SensitivePlaceholder);

        var nameParts = (names ?? [])
            .SelectMany(n => n.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(p => p.Length >= MinNameLength && p.All(c => char.IsLetter(c) || c is '-' or '\''))
            .Select(TurkishText.Fold)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(p => p.Length)
            .ToList();

        if (nameParts.Count > 0)
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}])(?<value>{string.Join('|', nameParts.Select(Regex.Escape))})(?![\p{{L}}\p{{N}}])";
            var nameRegex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            text = ReplaceFolded(text, nameRegex, "value", RedactionKind.Name, counts, NamePlaceholder);
        }

        return new RedactionResult(text, counts);
    }

    private static string Replace(
        string text,
        Regex regex,
        RedactionKind kind,
        Dictionary<RedactionKind, int> counts,
        Func<Match, string?> replacement) =>
        regex.Replace(text, match =>
        {
            var value = replacement(match);
            if (value is null)
            {
                return match.Value;
            }

            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            return value;
        });

    // Matches on the folded text and replaces the named group in the original text.
    private static string ReplaceFolded(
        string text,
        Regex regex,
        string group,
        RedactionKind kind,
        Dictionary<RedactionKind, int> counts,
        string placeholder)
    {
        var matches = regex.Matches(TurkishText.Fold(text));
        if (matches.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var value = matches[i].Groups[group];
            var trimmedLength = value.Value.TrimEnd().Length;
            if (trimmedLength == 0)
            {
                continue;
            }

            builder.Remove(value.Index, trimmedLength).Insert(value.Index, placeholder);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"(?:\b(?:https?://|www\.)\S+)|(?:\b(?:linkedin|github|gitlab|behance|dribbble|medium|twitter|x|instagram|facebook|kaggle|stackoverflow)\.com/\S+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        1000)]
    private static partial Regex UrlRegex();

    // Country code, check digits, then groups of four (optionally space separated) and a short tail.
    [GeneratedRegex(@"\b[A-Za-z]{2}\d{2}(?:[ ]?[A-Za-z0-9]{4}){2,7}(?:[ ]?[A-Za-z0-9]{1,4})?\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ElevenDigitsRegex();

    // International numbers starting with + or 00, Turkish 0XXX XXX XX XX (with optional +90, parentheses and
    // separators) and XXX XXX XXXX. Date ranges such as "01.2015 - 06.2020" do not fit these groupings.
    [GeneratedRegex(
        @"(?<![\w+])(?:(?:\+|00)\d{1,3}[\s.\-]?\(?\d{2,4}\)?(?:[\s.\-]?\d{2,4}){2,4}|\(?0?\d{3}\)?[\s.\-]?\d{3}[\s.\-]?\d{2}[\s.\-]?\d{2}|\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4})(?![\w])",
        RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex PhoneRegex();

    // Labels are written in folded form (see TurkishText.Fold). The value runs to the end of the line or to a | or ; separator.
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:dogum tarihi|dogum yeri|dogum yili|d\. ?tarihi|yasi?|cinsiyeti?|medeni durumu?|medeni hali?|uyruk|uyrugu|milliyeti?|dini?|inanci?|saglik durumu|saglik|engel durumu|engellilik durumu|engellilik|engel|kan grubu|askerlik durumu|askerlik|boy|kilo|cocuk sayisi|cocuklar|esi?|ev adresi|adresi?|ikametgah|anne adi|baba adi|sigara|sabika kaydi|sabika|date of birth|birth ?date|dob|place of birth|birth ?place|age|gender|sex|marital status|nationality|citizenship|religion|health status|health|disability|blood type|blood group|military service|military status|height|weight|children|spouse|home address|address|smoker|criminal record)\s*[:：]\s*(?<value>[^|;\r\n]*)",
        RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex SensitiveFieldRegex();
}
