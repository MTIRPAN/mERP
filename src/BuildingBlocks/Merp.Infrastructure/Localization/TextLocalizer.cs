using System.Globalization;
using System.Text;
using Merp.Application.Abstractions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.Localization;

/// <summary>
/// The request's view of the tenant's cultures and translations. <c>RequestContextMiddleware</c> loads it once the
/// tenant is known; until then (and outside requests) it holds the embedded texts only.
/// </summary>
public sealed class LocalizationContext
{
    public LocalizationSnapshot Snapshot { get; private set; } = LocalizationSnapshot.Embedded;

    public void Set(LocalizationSnapshot snapshot) => Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}

/// <summary>
/// Resolves keys over the culture chain of <see cref="LocalizationSnapshot.Chain"/>: in each culture the tenant's
/// database text wins over the embedded one (D11, D28).
/// </summary>
public sealed class TextLocalizer(EmbeddedTexts embedded, LocalizationContext context) : ITextLocalizer
{
    public string Get(string key, IReadOnlyDictionary<string, object?>? args = null, string? culture = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        culture ??= CultureInfo.CurrentUICulture.Name;
        var snapshot = context.Snapshot;

        foreach (var code in snapshot.Chain(culture))
        {
            if ((snapshot.Text(code, key) ?? embedded.Get(code, key)) is { } text)
            {
                return args is null || args.Count == 0 ? text : Format(text, args, CultureInfo.GetCultureInfo(culture));
            }
        }

        return key;
    }

    public IReadOnlyDictionary<string, string> GetAll(string prefix, string culture)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        var snapshot = context.Snapshot;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // From the last fallback to the culture itself, so nearer cultures overwrite
        foreach (var code in snapshot.Chain(culture).Reverse())
        {
            Merge(embedded.Texts(code));
            if (snapshot.Texts.TryGetValue(code, out var texts))
            {
                Merge(texts);
            }
        }

        return result;

        void Merge(IReadOnlyDictionary<string, string> texts)
        {
            foreach (var (key, value) in texts.Where(t => t.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                result[key] = value;
            }
        }
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

/// <summary>Hosts without the Platform module: the embedded Turkish texts are the only culture.</summary>
internal sealed class EmbeddedLocalizationSource : ILocalizationSource
{
    public Task<LocalizationSnapshot> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(LocalizationSnapshot.Embedded);
}

public static class LocalizationServiceCollectionExtensions
{
    /// <summary>Embedded texts, the per-request context and the localizer; Platform replaces the source.</summary>
    public static IServiceCollection AddMerpLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<EmbeddedTexts>();
        services.TryAddSingleton<IEmbeddedTexts>(sp => sp.GetRequiredService<EmbeddedTexts>());
        services.TryAddScoped<LocalizationContext>();
        services.TryAddScoped<ITextLocalizer, TextLocalizer>();
        services.TryAddScoped<ILocalizationSource, EmbeddedLocalizationSource>();
        return services;
    }
}
