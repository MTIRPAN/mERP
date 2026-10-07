using System.ComponentModel;
using System.Globalization;

namespace Merp.Application.Abstractions.Localization;

/// <summary>A culture the tenant has switched on (D11).</summary>
/// <remarks>FallbackCode is the culture whose texts stand in for missing ones; null falls back to the default culture.</remarks>
public sealed record CultureDefinition(string Code, string Name, string? FallbackCode = null);

/// <summary>
/// The tenant's active cultures and its translations from the database (D11, D28): the default culture, the active
/// cultures and the texts that override or add to the embedded defaults, per culture and key.
/// </summary>
// Immutable: HybridCache hands the same instance to every request instead of copying it
[ImmutableObject(true)]
public sealed class LocalizationSnapshot(
    string defaultCulture,
    IReadOnlyList<CultureDefinition> cultures,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts)
{
    /// <summary>Culture of the embedded texts; the last fallback whatever the tenant's settings.</summary>
    public const string EmbeddedCulture = "tr-TR";

    /// <summary>No tenant (sign-in screen, background work without a tenant): embedded Turkish texts only.</summary>
    public static readonly LocalizationSnapshot Embedded = new(
        EmbeddedCulture,
        [new CultureDefinition(EmbeddedCulture, CultureInfo.GetCultureInfo(EmbeddedCulture).NativeName)],
        new Dictionary<string, IReadOnlyDictionary<string, string>>());

    public string DefaultCulture { get; } = defaultCulture;

    /// <summary>Active cultures, the default first.</summary>
    public IReadOnlyList<CultureDefinition> Cultures { get; } = cultures;

    /// <summary>Database texts per culture and key.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts { get; } = texts;

    public bool IsActive(string? culture) => Find(culture) is not null;

    /// <summary>The active culture with this code, compared case-insensitively.</summary>
    public CultureDefinition? Find(string? culture) =>
        culture is null ? null : Cultures.FirstOrDefault(c => string.Equals(c.Code, culture, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The best active culture for a requested one: the same code, otherwise the first active culture of the same
    /// language (<c>en</c> or <c>en-GB</c> → <c>en-US</c>); null when none fits.
    /// </summary>
    public string? Match(string requested)
    {
        if (Find(requested) is { } exact)
        {
            return exact.Code;
        }

        var language = requested.Split('-')[0];
        return Cultures.FirstOrDefault(c => string.Equals(c.Code.Split('-')[0], language, StringComparison.OrdinalIgnoreCase))?.Code;
    }

    /// <summary>
    /// Cultures to look a text up in, in order: the culture, its fallbacks, the default culture and the embedded
    /// culture. Each appears once, so a fallback cycle cannot loop.
    /// </summary>
    public IReadOnlyList<string> Chain(string culture)
    {
        var chain = new List<string>();
        void Add(string code)
        {
            if (!chain.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                chain.Add(code);
            }
        }

        var current = Find(culture)?.Code ?? culture;
        while (!chain.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            Add(current);
            if (Find(current)?.FallbackCode is not { } fallback)
            {
                break;
            }

            current = fallback;
        }

        Add(DefaultCulture);
        Add(EmbeddedCulture);
        return chain;
    }

    /// <summary>Database text of a key in exactly this culture.</summary>
    public string? Text(string culture, string key) =>
        Texts.TryGetValue(culture, out var texts) && texts.TryGetValue(key, out var text) ? text : null;
}

/// <summary>Loads the current tenant's <see cref="LocalizationSnapshot"/>; Platform implements it over its tables.</summary>
public interface ILocalizationSource
{
    Task<LocalizationSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Default texts embedded in the modules (<c>Localization/{culture}.json</c>).</summary>
public interface IEmbeddedTexts
{
    /// <summary>Every key of the embedded culture, ordered; the catalog of translatable texts.</summary>
    IReadOnlyList<string> Keys { get; }

    string? Get(string culture, string key);
}
