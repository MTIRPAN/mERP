using System.Globalization;
using Merp.Application.Abstractions.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class LocalizationEndpoints
{
    public const string WebPrefix = "web.";

    /// <summary>
    /// Texts of the web client (<c>web.*</c> keys only). Anonymous because the sign-in screen needs them (D20).
    /// Database overrides and culture management follow in PLAN P4.
    /// </summary>
    public static void MapLocalizationEndpoints(this IEndpointRouteBuilder group) =>
        group.MapGet("/localization/bundles/{culture}", GetBundle)
            .AllowAnonymous()
            .WithName("GetLocalizationBundle")
            .WithSummary("Web client texts for a culture");

    private static IResult GetBundle(string culture, ITextLocalizer localizer, IConfiguration configuration, HttpContext context)
    {
        var supported = configuration.GetSection("Localization:SupportedCultures").Get<string[]>() ?? ["tr-TR"];
        var match = supported.FirstOrDefault(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "public, max-age=300";
        context.Response.Headers.ContentLanguage = CultureInfo.GetCultureInfo(match).Name;
        return Results.Ok(localizer.GetAll(WebPrefix, match));
    }
}
