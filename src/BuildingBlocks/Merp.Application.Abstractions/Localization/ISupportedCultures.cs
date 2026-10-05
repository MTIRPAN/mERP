namespace Merp.Application.Abstractions.Localization;

/// <summary>Active cultures of the installation (D11), from <c>Localization:SupportedCultures</c>; the first is the default.</summary>
public interface ISupportedCultures
{
    IReadOnlyList<string> All { get; }

    bool IsSupported(string culture);
}
