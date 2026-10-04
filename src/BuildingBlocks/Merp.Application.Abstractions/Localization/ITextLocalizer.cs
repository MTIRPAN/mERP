namespace Merp.Application.Abstractions.Localization;

/// <summary>
/// Resolves localization keys (<c>{module}.{area}.{key}</c>, D11) to text in the current culture, falling back to
/// the default culture. Placeholders are named: <c>"En fazla {maxLength} karakter"</c>.
/// </summary>
public interface ITextLocalizer
{
    /// <summary>Text for <paramref name="key"/>; the key itself when no text exists.</summary>
    string Get(string key, IReadOnlyDictionary<string, object?>? args = null, string? culture = null);
}
