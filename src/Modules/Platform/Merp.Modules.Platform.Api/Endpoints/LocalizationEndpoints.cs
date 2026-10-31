using System.Globalization;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Infrastructure.Localization;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Localization;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>Web texts (D20) and the tenant's languages and translations (PLAN P4, D11, D28).</summary>
internal static class LocalizationEndpoints
{
    public const string WebPrefix = "web.";

    // Room for the 5 MB file plus the multipart framing
    private const long MaxImportRequestBytes = ImportTextsCommand.MaxFileBytes + (64 * 1024);

    public static void MapLocalizationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/localization/bundles/{culture}", GetBundle)
            .AllowAnonymous()
            .WithName("GetLocalizationBundle")
            .WithSummary("Web client texts for a culture")
            .WithDescription("Anonymous for the sign-in screen: then only the embedded texts. With a token, the tenant's translations are included and only its active cultures are served.");

        var cultures = group.MapGroup("/localization/cultures");
        cultures.MapGet("/", ListCulturesAsync)
            .RequirePermission(PlatformPermissions.Localization.Read)
            .WithName("ListCultures")
            .WithSummary("Languages of the tenant with their translation coverage")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cultures.MapPost("/", AddCultureAsync)
            .RequirePermission(PlatformPermissions.Localization.Manage)
            .WithName("AddCulture")
            .WithSummary("Add a language")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cultures.MapPut("/{code}", UpdateCultureAsync)
            .RequirePermission(PlatformPermissions.Localization.Manage)
            .WithName("UpdateCulture")
            .WithSummary("Rename a language, switch it on or off, set its fallback")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        cultures.MapDelete("/{code}", DeleteCultureAsync)
            .RequirePermission(PlatformPermissions.Localization.Manage)
            .WithName("DeleteCulture")
            .WithSummary("Delete a language and its translations")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var texts = group.MapGroup("/localization/texts/{culture}");
        texts.MapGet("/", ListTextsAsync)
            .RequirePermission(PlatformPermissions.Localization.Read)
            .WithName("ListTexts")
            .WithSummary("The text catalog in a language, optionally only the missing translations")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        texts.MapPut("/", SetTextAsync)
            .RequirePermission(PlatformPermissions.Localization.Manage)
            .WithName("SetText")
            .WithSummary("Translate one key; an empty value removes the translation")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        texts.MapPost("/bulk", SetTextsAsync)
            .RequirePermission(PlatformPermissions.Localization.Manage)
            .WithName("SetTexts")
            .WithSummary("Translate many keys at once, e.g. reviewed AI drafts (A7, D60); returns how many changed")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        texts.MapPost("/drafts", DraftAsync)
            .RequirePermission(PlatformPermissions.Localization.Manage)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("DraftTranslations")
            .WithSummary("Draft up to 100 missing translations of the filter with AI from the default language (A7, D60)")
            .WithDescription("Nothing is stored: a user reviews the drafts and saves them with SetTexts.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        texts.MapGet("/export", ExportAsync)
            .RequirePermission(PlatformPermissions.Localization.Read)
            .WithName("ExportTexts")
            .WithSummary("The text catalog of a language as an Excel file")
            .Produces(StatusCodes.Status200OK, contentType: ExportedFile.XlsxContentType)
            .ProducesProblem(StatusCodes.Status404NotFound);

        texts.MapPost("/import", ImportAsync)
            .RequirePermission(PlatformPermissions.Localization.Manage)
            .WithMetadata(new RequestSizeLimitAttribute(MaxImportRequestBytes))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("ImportTexts")
            .WithSummary("Import translations from the Excel file of the export")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Results<Ok<IReadOnlyDictionary<string, string>>, NotFound> GetBundle(
        string culture,
        ITextLocalizer localizer,
        LocalizationContext localization,
        HttpContext context)
    {
        var snapshot = localization.Snapshot;
        string? match;
        if (ReferenceEquals(snapshot, LocalizationSnapshot.Embedded))
        {
            // No tenant yet (sign-in screen): any known culture, served from the embedded texts with fallback
            match = CultureCodes.Normalize(culture);
            context.Response.Headers.CacheControl = "public, max-age=300";
        }
        else
        {
            match = snapshot.Find(culture)?.Code;
            // Tenant translations change at any time and differ per tenant
            context.Response.Headers.CacheControl = "private, no-cache";
        }

        if (match is null)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.ContentLanguage = CultureInfo.GetCultureInfo(match).Name;
        return TypedResults.Ok(localizer.GetAll(WebPrefix, match));
    }

    private static async Task<Results<Ok<IReadOnlyList<CultureItem>>, ProblemHttpResult>> ListCulturesAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetCulturesQuery(), cancellationToken));

    private static async Task<Results<Created<CultureCreatedResponse>, ProblemHttpResult>> AddCultureAsync(
        AddCultureCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/localization/cultures/{result.Value}", new CultureCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateCultureAsync(
        string code,
        UpdateCultureCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContent(await sender.Send(command with { Code = code }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteCultureAsync(string code, ISender sender, CancellationToken cancellationToken) =>
        NoContent(await sender.Send(new DeleteCultureCommand(code), cancellationToken));

    /// <param name="culture">Culture code, e.g. en-US.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="search">Part of the key, the default text or the translation.</param>
    /// <param name="missing">Only keys without a text of their own in the culture.</param>
    /// <param name="prefix">Keys starting with this, e.g. web. or platform.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<TextItem>>, ProblemHttpResult>> ListTextsAsync(
        string culture,
        int? page,
        int? pageSize,
        string? search,
        bool? missing,
        string? prefix,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetTextsQuery(culture, new PageRequest(page, pageSize), search, missing ?? false, prefix), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> SetTextAsync(
        string culture,
        SetTextCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContent(await sender.Send(command with { Culture = culture }, cancellationToken));

    private static async Task<Results<Ok<SetTextsResponse>, ProblemHttpResult>> SetTextsAsync(
        string culture,
        SetTextsCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Culture = culture }, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new SetTextsResponse(result.Value)) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Ok<TranslationDraftResponse>, ProblemHttpResult>> DraftAsync(
        string culture,
        DraftTranslationsQuery query,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(query with { Culture = culture }, cancellationToken));

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(string culture, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ExportTextsQuery(culture), cancellationToken);
        return result.IsSuccess
            ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="culture">Culture code, e.g. en-US.</param>
    /// <param name="file">The xlsx file of the export with column C translated, at most 5 MB.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<ImportTextsResult>, ProblemHttpResult>> ImportAsync(
        string culture,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var content = Array.Empty<byte>();
        if (file is { Length: > 0 and <= ImportTextsCommand.MaxFileBytes })
        {
            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        return Respond(await sender.Send(new ImportTextsCommand(culture, content), cancellationToken));
    }

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<NoContent, ProblemHttpResult> NoContent(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}

public sealed record CultureCreatedResponse(string Code);

/// <summary>How many translations changed.</summary>
public sealed record SetTextsResponse(int Changed);
