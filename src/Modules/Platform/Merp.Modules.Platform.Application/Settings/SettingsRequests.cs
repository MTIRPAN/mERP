using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Settings;

/// <summary>Settings of the Platform module (D35).</summary>
public sealed class PlatformSettingDefinitions : ISettingDefinitionProvider
{
    /// <summary>E-mail copies of notifications; off keeps them in the bell only (D32).</summary>
    public const string EmailNotificationsEnabled = "platform.notifications.email_enabled";

    public IEnumerable<SettingDefinition> GetSettings() =>
    [
        SettingDefinition.Boolean(
            EmailNotificationsEnabled,
            defaultValue: true,
            "platform.settings.notifications.email_enabled",
            "platform.settings.notifications.email_enabled_hint",
            companyOverridable: true),
    ];
}

/// <summary>Clears the tenant's cached setting values; call it after the command commits.</summary>
public interface ISettingsCache
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

public sealed record SettingOptionItem(string Value, string Label);

/// <remarks>
/// Value is set at the level shown (null: not set there); InheritedValue is what applies without it: the tenant's
/// value or the default for a company, the default for the tenant.
/// </remarks>
public sealed record SettingItem(
    string Key,
    string Label,
    string? Description,
    SettingType Type,
    IReadOnlyList<SettingOptionItem> Options,
    decimal? Min,
    decimal? Max,
    bool WholeNumber,
    int? MaxLength,
    bool CompanyOverridable,
    JsonElement? Value,
    JsonElement InheritedValue,
    JsonElement DefaultValue);

public sealed record SettingModuleGroup(string Module, string Name, IReadOnlyList<SettingItem> Settings);

/// <summary>
/// The settings of all modules with their values for the tenant, or for a company (only those a company may
/// override), grouped by module.
/// </summary>
public sealed record GetSettingsQuery(Guid? CompanyId) : IQuery<IReadOnlyList<SettingModuleGroup>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Settings.Read;
}

/// <summary>
/// Saves values for the tenant or a company; a null value removes it, so the level above applies again. Keys not
/// given keep their values.
/// </summary>
public sealed record UpdateSettingsCommand(Guid? CompanyId, IReadOnlyDictionary<string, JsonElement?>? Values) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Settings.Manage;
}

public static class SettingErrors
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.settings.errors.tenant_scope_required");
    public const string UnknownCompanyCode = "platform.settings.errors.unknown_company";
    public const string UnknownSettingCode = "platform.settings.errors.unknown_setting";
    public const string NotCompanySettingCode = "platform.settings.errors.not_company_setting";
    public const string InvalidValueCode = "platform.settings.errors.invalid_value";
    public const string OutOfRangeCode = "platform.settings.errors.out_of_range";
    public const string TooLongCode = "platform.settings.errors.too_long";
}

internal static class SettingRules
{
    /// <summary>The value as stored JSON, or the error code of a value that does not fit the definition.</summary>
    public static (string? Json, string? Error, IReadOnlyDictionary<string, object?>? Args) Check(SettingDefinition definition, JsonElement value)
    {
        switch (definition.Type)
        {
            case SettingType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return (value.GetRawText(), null, null);
            case SettingType.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number):
                if (definition.WholeNumber && number != decimal.Truncate(number))
                {
                    return (null, SettingErrors.InvalidValueCode, null);
                }

                return (definition.Min is { } min && number < min) || (definition.Max is { } max && number > max)
                    ? (null, SettingErrors.OutOfRangeCode, new Dictionary<string, object?> { ["min"] = definition.Min, ["max"] = definition.Max })
                    : (JsonSerializer.Serialize(number), null, null);
            case SettingType.Text when value.ValueKind == JsonValueKind.String:
                var text = value.GetString()!.Trim();
                return definition.MaxLength is { } length && text.Length > length
                    ? (null, SettingErrors.TooLongCode, new Dictionary<string, object?> { ["maxLength"] = length })
                    : (JsonSerializer.Serialize(text), null, null);
            case SettingType.Choice when value.ValueKind == JsonValueKind.String
                && (definition.Options ?? []).Any(o => string.Equals(o.Value, value.GetString(), StringComparison.Ordinal)):
                return (value.GetRawText(), null, null);
            default:
                return (null, SettingErrors.InvalidValueCode, null);
        }
    }
}

internal sealed class GetSettingsQueryHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    SettingDefinitionRegistry registry,
    ITextLocalizer localizer) : IQueryHandler<GetSettingsQuery, IReadOnlyList<SettingModuleGroup>>
{
    public async Task<Result<IReadOnlyList<SettingModuleGroup>>> Handle(GetSettingsQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return SettingErrors.TenantScopeRequired;
        }

        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var profiles = await db.SettingsProfiles.AsNoTracking()
            .Where(p => p.CompanyId == null || p.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);
        var tenant = profiles.SingleOrDefault(p => p.CompanyId is null)?.Values ?? new Dictionary<string, string>();
        var company = request.CompanyId is null ? null : profiles.SingleOrDefault(p => p.CompanyId == request.CompanyId)?.Values;
        static JsonElement? Json(IReadOnlyDictionary<string, string>? values, string key) =>
            values is not null && values.TryGetValue(key, out var json) ? JsonDocument.Parse(json).RootElement.Clone() : null;

        var culture = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);
        return registry.All
            .Where(s => request.CompanyId is null || s.CompanyOverridable)
            .GroupBy(s => s.Module, StringComparer.Ordinal)
            .Select(module => new SettingModuleGroup(
                module.Key,
                localizer.Get("permissions." + module.Key),
                [.. module.OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal).Select(s => new SettingItem(
                    s.Key,
                    localizer.Get(s.LabelKey),
                    s.DescriptionKey is null ? null : localizer.Get(s.DescriptionKey),
                    s.Type,
                    [.. (s.Options ?? []).Select(o => new SettingOptionItem(o.Value, localizer.Get(o.LabelKey)))],
                    s.Min,
                    s.Max,
                    s.WholeNumber,
                    s.MaxLength,
                    s.CompanyOverridable,
                    request.CompanyId is null ? Json(tenant, s.Key) : Json(company, s.Key),
                    request.CompanyId is null ? s.DefaultValue : Json(tenant, s.Key) ?? s.DefaultValue,
                    s.DefaultValue))]))
            .OrderBy(g => g.Name, culture)
            .ToList();
    }
}

internal sealed class UpdateSettingsCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    SettingDefinitionRegistry registry,
    ISettingsCache cache,
    IAfterCommit afterCommit) : ICommandHandler<UpdateSettingsCommand>
{
    public async Task<Result> Handle(UpdateSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return SettingErrors.TenantScopeRequired;
        }

        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            return Error.Validation([new FieldError("companyId", SettingErrors.UnknownCompanyCode)]);
        }

        var errors = new List<FieldError>();
        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in request.Values ?? new Dictionary<string, JsonElement?>())
        {
            var field = "values." + key;
            if (registry.Find(key) is not { } definition)
            {
                errors.Add(new FieldError(field, SettingErrors.UnknownSettingCode));
                continue;
            }

            if (request.CompanyId is not null && !definition.CompanyOverridable)
            {
                errors.Add(new FieldError(field, SettingErrors.NotCompanySettingCode));
                continue;
            }

            if (value is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } given)
            {
                changes[key] = null;
                continue;
            }

            var (json, error, args) = SettingRules.Check(definition, given);
            if (error is not null)
            {
                errors.Add(new FieldError(field, error, args ?? Error.NoArgs));
            }
            else
            {
                changes[key] = json;
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var profile = await db.SettingsProfiles.SingleOrDefaultAsync(p => p.CompanyId == request.CompanyId, cancellationToken);
        if (profile is null)
        {
            profile = SettingsProfile.Create(request.CompanyId);
            db.SettingsProfiles.Add(profile);
        }

        profile.Apply(changes);
        afterCommit.Enqueue(cache.InvalidateAsync);
        return Result.Success();
    }
}
