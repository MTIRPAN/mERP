using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Localization;

namespace Merp.Infrastructure.Localization;

/// <summary>
/// Default texts from the <c>Localization/{culture}.json</c> files embedded in each module (D11). Database overrides
/// and additional cultures arrive with PLAN P4.
/// </summary>
public sealed class EmbeddedTextLocalizer(LocalizationResources resources) : ITextLocalizer
{
    public const string DefaultCulture = "tr-TR";

    private readonly ConcurrentDictionary<string, FrozenDictionary<string, string>> _cultures = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string key, IReadOnlyDictionary<string, object?>? args = null, string? culture = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        culture ??= CultureInfo.CurrentUICulture.Name;

        if (!Texts(culture).TryGetValue(key, out var text)
            && !Texts(DefaultCulture).TryGetValue(key, out text))
        {
            return key;
        }

        return args is null || args.Count == 0 ? text : Format(text, args, CultureInfo.GetCultureInfo(culture));
    }

    private FrozenDictionary<string, string> Texts(string culture) => _cultures.GetOrAdd(culture, Load);

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

    // Replaces {name} placeholders; unknown placeholders are left as they are.
    private static string Format(string text, IReadOnlyDictionary<string, object?> args, CultureInfo culture)
    {
        var builder = new StringBuilder(text.Length + 16);
        var position = 0;
        while (position < text.Length)
        {
            var open = text.IndexOf('{', position);
            var close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                builder.Append(text, position, text.Length - position);
                break;
            }

            builder.Append(text, position, open - position);
            var name = text.Substring(open + 1, close - open - 1);
            if (args.TryGetValue(name, out var value))
            {
                builder.Append(value is IFormattable formattable ? formattable.ToString(null, culture) : value?.ToString());
            }
            else
            {
                builder.Append(text, open, close - open + 1);
            }

            position = close + 1;
        }

        return builder.ToString();
    }
}
