using System.Globalization;
using FluentValidation;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Domain.Localization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Localization;

/// <summary>All cultures of the tenant, active or not, with how much of the text catalog each covers.</summary>
public sealed record GetCulturesQuery : IQuery<IReadOnlyList<CultureItem>>, IRequirePermission
{
    public string Permission => PlatformPermissions.Localization.Read;
}

/// <remarks>TranslatedCount counts the catalog keys that have a text in the culture itself (database or embedded).</remarks>
public sealed record CultureItem(
    string Code,
    string Name,
    bool IsActive,
    bool IsDefault,
    string? FallbackCode,
    int TranslatedCount,
    int TotalCount,
    uint Version);

/// <summary>
/// The text catalog in one culture: every key of the embedded texts with the default culture's text and the
/// culture's own text. Missing keeps the keys without a text of their own (they fall back).
/// </summary>
public sealed record GetTextsQuery(string Culture, PageRequest Paging, string? Search = null, bool Missing = false, string? Prefix = null)
    : IQuery<PagedResult<TextItem>>, IRequirePermission
{
    public string Permission => PlatformPermissions.Localization.Read;
}

/// <remarks>Source is Database (a translation of the tenant), Embedded (shipped with the product) or null (missing).</remarks>
public sealed record TextItem(string Key, string? DefaultText, string? Text, string? Source);

/// <summary>The catalog of a culture as an Excel file: key, default culture text, the culture's text.</summary>
public sealed record ExportTextsQuery(string Culture) : IQuery<ExportedFile>, IRequirePermission
{
    public string Permission => PlatformPermissions.Localization.Read;
}

public sealed record ExportedFile(string FileName, string ContentType, byte[] Content)
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public static class TextSources
{
    public const string Database = nameof(Database);
    public const string Embedded = nameof(Embedded);
}

internal sealed class GetTextsQueryValidator : AbstractValidator<GetTextsQuery>
{
    public GetTextsQueryValidator()
    {
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Prefix).MaximumLength(CultureText.KeyMaxLength);
    }
}

/// <summary>The catalog of a culture with the tenant's texts, read straight from the tables (inactive cultures too).</summary>
internal sealed class TextCatalog(IPlatformDbContext db, IEmbeddedTexts embedded)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public sealed record Entry(string Key, string? DefaultText, string? Text, string? Source);

    /// <summary>The entries of the list's filters: only the missing translations, a key prefix, a search term.</summary>
    public static IEnumerable<Entry> Filter(IEnumerable<Entry> entries, bool missing, string? prefix, string? search)
    {
        if (missing)
        {
            entries = entries.Where(e => e.Text is null);
        }

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            entries = entries.Where(e => e.Key.StartsWith(prefix.Trim(), StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            entries = entries.Where(e => Contains(e.Key, term) || Contains(e.DefaultText, term) || Contains(e.Text, term));
        }

        return entries;
    }

    private static bool Contains(string? text, string term) =>
        text is not null && Turkish.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase) >= 0;

    public async Task<(Culture Culture, string DefaultCode, IReadOnlyList<Entry> Entries)?> LoadAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = CultureCodes.Normalize(code);
        var cultures = await db.Cultures.AsNoTracking().Include(c => c.Texts)
            .Where(c => c.Code == normalized || c.IsDefault)
            .ToListAsync(cancellationToken);
        if (cultures.FirstOrDefault(c => c.Code == normalized) is not { } culture)
        {
            return null;
        }

        var @default = cultures.FirstOrDefault(c => c.IsDefault);
        var defaultCode = @default?.Code ?? LocalizationSnapshot.EmbeddedCulture;
        var own = culture.Texts.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var defaults = @default?.Texts.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal) ?? [];

        return (culture, defaultCode, [.. embedded.Keys.Select(key =>
        {
            var defaultText = defaults.GetValueOrDefault(key) ?? embedded.Get(defaultCode, key) ?? embedded.Get(LocalizationSnapshot.EmbeddedCulture, key);
            if (own.TryGetValue(key, out var text))
            {
                return new Entry(key, defaultText, text, TextSources.Database);
            }

            return embedded.Get(culture.Code, key) is { } shipped
                ? new Entry(key, defaultText, shipped, TextSources.Embedded)
                : new Entry(key, defaultText, null, null);
        })]);
    }
}

internal sealed class GetCulturesQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes, IEmbeddedTexts embedded)
    : IQueryHandler<GetCulturesQuery, IReadOnlyList<CultureItem>>
{
    public async Task<Result<IReadOnlyList<CultureItem>>> Handle(GetCulturesQuery request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        var cultures = await db.Cultures.AsNoTracking()
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.Code)
            .Select(c => new
            {
                c.Code,
                c.Name,
                c.IsActive,
                c.IsDefault,
                c.FallbackCode,
                Keys = c.Texts.Select(t => t.Key).ToList(),
                Version = EF.Property<uint>(c, ConcurrencyVersion.PropertyName),
            })
            .ToListAsync(cancellationToken);

        var catalog = embedded.Keys;
        return cultures.Select(c =>
        {
            var own = c.Keys.ToHashSet(StringComparer.Ordinal);
            var translated = catalog.Count(key => own.Contains(key) || embedded.Get(c.Code, key) is not null);
            return new CultureItem(c.Code, c.Name, c.IsActive, c.IsDefault, c.FallbackCode, translated, catalog.Count, c.Version);
        }).ToList();
    }
}

internal sealed class GetTextsQueryHandler(IPlatformDbContext db, IEmbeddedTexts embedded, IAccessScopeProvider scopes)
    : IQueryHandler<GetTextsQuery, PagedResult<TextItem>>
{
    public async Task<Result<PagedResult<TextItem>>> Handle(GetTextsQuery request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        if (await new TextCatalog(db, embedded).LoadAsync(request.Culture, cancellationToken) is not { } loaded)
        {
            return CommonErrors.NotFound;
        }

        var list = TextCatalog.Filter(loaded.Entries, request.Missing, request.Prefix, request.Search).ToList();
        return new PagedResult<TextItem>(
            [.. list.Skip(request.Paging.Skip).Take(request.Paging.Size).Select(e => new TextItem(e.Key, e.DefaultText, e.Text, e.Source))],
            request.Paging.PageNumber,
            request.Paging.Size,
            list.Count);
    }
}

internal sealed class ExportTextsQueryHandler(IPlatformDbContext db, IEmbeddedTexts embedded, IAccessScopeProvider scopes, ISpreadsheet spreadsheet)
    : IQueryHandler<ExportTextsQuery, ExportedFile>
{
    public async Task<Result<ExportedFile>> Handle(ExportTextsQuery request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        if (await new TextCatalog(db, embedded).LoadAsync(request.Culture, cancellationToken) is not { } loaded)
        {
            return CommonErrors.NotFound;
        }

        // Column C is headed by the culture code; the import checks it (ImportTextsCommand)
        var content = spreadsheet.Write(
            "Translations",
            ["Key", loaded.DefaultCode, loaded.Culture.Code],
            loaded.Entries.Select(e => (IReadOnlyList<string?>)[e.Key, e.DefaultText, e.Text]));
        return new ExportedFile($"translations-{loaded.Culture.Code}.xlsx", ExportedFile.XlsxContentType, content);
    }
}
