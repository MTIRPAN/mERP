using Merp.Application.Abstractions.Localization;
using Microsoft.Extensions.Configuration;

namespace Merp.Infrastructure.Localization;

public sealed class SupportedCultures(IConfiguration configuration) : ISupportedCultures
{
    public IReadOnlyList<string> All { get; } =
        configuration.GetSection("Localization:SupportedCultures").Get<string[]>() is { Length: > 0 } cultures
            ? cultures
            : [EmbeddedTextLocalizer.DefaultCulture];

    public bool IsSupported(string culture) => All.Contains(culture, StringComparer.OrdinalIgnoreCase);
}
