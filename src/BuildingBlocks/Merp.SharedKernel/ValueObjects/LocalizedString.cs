using System.Collections.ObjectModel;
using System.Globalization;
using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Master data text in several cultures, stored as jsonb <c>{"tr-TR": "...", "en-US": "..."}</c> (D11).
/// Missing translations fall back to the default culture.
/// </summary>
public sealed class LocalizedString : IEquatable<LocalizedString>
{
    private readonly ReadOnlyDictionary<string, string> _values;

    private LocalizedString(IDictionary<string, string> values) =>
        _values = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string> Values => _values;

    public static Result<LocalizedString> Create(IReadOnlyDictionary<string, string?>? values)
    {
        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (culture, text) in values ?? new Dictionary<string, string?>())
        {
            if (!IsCultureName(culture))
            {
                return CommonErrors.InvalidCulture;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                cleaned[NormalizeCulture(culture)] = text.Trim();
            }
        }

        return cleaned.Count == 0 ? CommonErrors.Required : new LocalizedString(cleaned);
    }

    public static LocalizedString Of(string culture, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new LocalizedString(new Dictionary<string, string> { [NormalizeCulture(culture)] = text.Trim() });
    }

    public bool Has(string culture) => _values.ContainsKey(culture);

    /// <summary>Text in <paramref name="culture"/>, otherwise in <paramref name="defaultCulture"/>, otherwise any text.</summary>
    public string Get(string culture, string defaultCulture)
    {
        if (_values.TryGetValue(culture, out var text) || _values.TryGetValue(defaultCulture, out text))
        {
            return text;
        }

        return _values.Values.First();
    }

    /// <summary>Returns a copy with the text for <paramref name="culture"/> set, or removed when empty.</summary>
    public LocalizedString With(string culture, string? text)
    {
        var values = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            values.Remove(culture);
        }
        else
        {
            values[NormalizeCulture(culture)] = text.Trim();
        }

        return values.Count == 0 ? this : new LocalizedString(values);
    }

    public bool Equals(LocalizedString? other) =>
        other is not null
        && _values.Count == other._values.Count
        && _values.All(pair => other._values.TryGetValue(pair.Key, out var text) && string.Equals(text, pair.Value, StringComparison.Ordinal));

    public override bool Equals(object? obj) => obj is LocalizedString other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (culture, text) in _values)
        {
            hash ^= HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(culture), text);
        }

        return hash;
    }

    private static bool IsCultureName(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return false;
        }

        try
        {
            return CultureInfo.GetCultureInfo(culture, predefinedOnly: true).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string NormalizeCulture(string culture) => CultureInfo.GetCultureInfo(culture).Name;
}
