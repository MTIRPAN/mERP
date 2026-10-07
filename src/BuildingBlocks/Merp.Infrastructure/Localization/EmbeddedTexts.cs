using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using Merp.Application.Abstractions.Localization;

namespace Merp.Infrastructure.Localization;

/// <summary>Default texts from the <c>Localization/{culture}.json</c> files embedded in each module (D11).</summary>
public sealed class EmbeddedTexts(LocalizationResources resources) : IEmbeddedTexts
{
    private readonly ConcurrentDictionary<string, FrozenDictionary<string, string>> _cultures = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string>? _keys;

    public IReadOnlyList<string> Keys => _keys ??= [.. Texts(LocalizationSnapshot.EmbeddedCulture).Keys.Order(StringComparer.Ordinal)];

    public string? Get(string culture, string key) => Texts(culture).GetValueOrDefault(key);

    /// <summary>All texts of one culture (without fallback).</summary>
    public IReadOnlyDictionary<string, string> Texts(string culture) => _cultures.GetOrAdd(culture, Load);

    private FrozenDictionary<string, string> Load(string culture)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var suffix = ".Localization." + culture + ".json";
        foreach (var assembly in resources.Assemblies)
        {
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
                foreach (var (key, value) in entries)
                {
                    texts[key] = value;
                }
            }
        }

        return texts.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
