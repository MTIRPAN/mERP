using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.CustomFields;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.CustomFields;

/// <summary>Entity types that can carry custom fields, with their localized names.</summary>
public sealed record GetCustomFieldEntityTypesQuery : IQuery<IReadOnlyList<CustomFieldEntityItem>>, IRequirePermission
{
    public string Permission => PlatformPermissions.CustomFields.Read;
}

public sealed record CustomFieldEntityItem(string Code, string Label);

/// <summary>Definitions of an entity type (or all), active or not, for the definition screen.</summary>
public sealed record GetCustomFieldDefinitionsQuery(string? EntityType) : IQuery<IReadOnlyList<CustomFieldDefinitionItem>>, IRequirePermission
{
    public string Permission => PlatformPermissions.CustomFields.Read;
}

public sealed record CustomFieldOptionItem(string Code, IReadOnlyDictionary<string, string> Label);

/// <remarks>Label and Group hold a text per culture; send Version back when updating.</remarks>
public sealed record CustomFieldDefinitionItem(
    Guid Id,
    string EntityType,
    string Code,
    CustomFieldType Type,
    Guid? CompanyId,
    string? CompanyName,
    IReadOnlyDictionary<string, string> Label,
    bool Required,
    IReadOnlyList<CustomFieldOptionItem> Options,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    bool WholeNumber,
    IReadOnlyDictionary<string, string>? Group,
    int Order,
    bool IsActive,
    uint Version);

/// <summary>
/// The fields a form shows for a record (D12): active definitions of the entity type for every company and for the
/// record's company, in order, with texts in the caller's language. Any signed-in user may read them (D22).
/// </summary>
public sealed record GetCustomFieldFormQuery(string EntityType, Guid? CompanyId) : IQuery<IReadOnlyList<CustomFieldFormItem>>;

public sealed record CustomFieldFormOption(string Code, string Label);

public sealed record CustomFieldFormItem(
    string Code,
    string Label,
    CustomFieldType Type,
    bool Required,
    IReadOnlyList<CustomFieldFormOption> Options,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    bool WholeNumber,
    string? Group);

internal sealed class GetCustomFieldEntityTypesQueryHandler(
    IEnumerable<ICustomFieldEntityProvider> entities,
    IAccessScopeProvider scopes,
    ITextLocalizer localizer) : IQueryHandler<GetCustomFieldEntityTypesQuery, IReadOnlyList<CustomFieldEntityItem>>
{
    public async Task<Result<IReadOnlyList<CustomFieldEntityItem>>> Handle(GetCustomFieldEntityTypesQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return CustomFieldErrors.TenantScopeRequired;
        }

        return entities.SelectMany(p => p.GetEntityTypes())
            .Select(e => new CustomFieldEntityItem(e.Code, localizer.Get(e.LabelKey)))
            .OrderBy(e => e.Label, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))
            .ToList();
    }
}

internal sealed class GetCustomFieldDefinitionsQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : IQueryHandler<GetCustomFieldDefinitionsQuery, IReadOnlyList<CustomFieldDefinitionItem>>
{
    public async Task<Result<IReadOnlyList<CustomFieldDefinitionItem>>> Handle(GetCustomFieldDefinitionsQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return CustomFieldErrors.TenantScopeRequired;
        }

        var query = db.CustomFieldDefinitions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            query = query.Where(d => d.EntityType == request.EntityType);
        }

        var rows = await query
            .Select(d => new { Definition = d, Version = EF.Property<uint>(d, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        var companyIds = rows.Select(r => r.Definition.CompanyId).OfType<Guid>().Distinct().Select(CompanyId.From).ToList();
        var companies = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id.Value, c => c.ShortName, cancellationToken);

        return rows
            .OrderBy(r => r.Definition.EntityType, StringComparer.Ordinal)
            .ThenBy(r => r.Definition.Order)
            .ThenBy(r => r.Definition.Code, StringComparer.Ordinal)
            .Select(r =>
            {
                var d = r.Definition;
                return new CustomFieldDefinitionItem(
                    d.Id.Value,
                    d.EntityType,
                    d.Code,
                    d.Type,
                    d.CompanyId,
                    d.CompanyId is { } id ? companies.GetValueOrDefault(id) : null,
                    d.Label.Values,
                    d.Required,
                    [.. d.Options.Select(o => new CustomFieldOptionItem(o.Code, o.Label))],
                    d.MaxLength,
                    d.Min,
                    d.Max,
                    d.WholeNumber,
                    d.Group?.Values,
                    d.Order,
                    d.IsActive,
                    r.Version);
            })
            .ToList();
    }
}

internal sealed class GetCustomFieldFormQueryHandler(IPlatformDbContext db, ICurrentUser currentUser, ILocalizationSource localization)
    : IQueryHandler<GetCustomFieldFormQuery, IReadOnlyList<CustomFieldFormItem>>
{
    public async Task<Result<IReadOnlyList<CustomFieldFormItem>>> Handle(GetCustomFieldFormQuery request, CancellationToken cancellationToken)
    {
        var definitions = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => d.EntityType == request.EntityType && d.IsActive && (d.CompanyId == null || d.CompanyId == request.CompanyId))
            .ToListAsync(cancellationToken);
        var defaultCulture = (await localization.GetAsync(cancellationToken)).DefaultCulture;
        string Text(IReadOnlyDictionary<string, string> texts) =>
            LocalizedString.Create(texts.ToDictionary(t => t.Key, string? (t) => t.Value)).Value.Get(currentUser.Culture, defaultCulture);

        return definitions
            .OrderBy(d => d.Order)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .Select(d => new CustomFieldFormItem(
                d.Code,
                d.Label.Get(currentUser.Culture, defaultCulture),
                d.Type,
                d.Required,
                [.. d.Options.Select(o => new CustomFieldFormOption(o.Code, Text(o.Label)))],
                d.MaxLength,
                d.Min,
                d.Max,
                d.WholeNumber,
                d.Group?.Get(currentUser.Culture, defaultCulture)))
            .ToList();
    }
}
