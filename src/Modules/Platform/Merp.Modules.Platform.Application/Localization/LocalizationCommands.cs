using System.Globalization;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Localization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Localization;

/// <summary>Clears the tenant's cached cultures and texts; call it after the command commits.</summary>
public interface ILocalizationCache
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adds a language to the tenant (D11). It starts active; texts fall back until translated.</summary>
/// <remarks>Code is a specific culture name such as <c>en-US</c>; Name defaults to the language's own name.</remarks>
public sealed record AddCultureCommand(string? Code, string? Name, string? FallbackCode) : ICommand<string>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Localization.Manage;
}

/// <summary>Renames a culture, switches it on or off and sets its fallback. The default culture stays active.</summary>
public sealed record UpdateCultureCommand(uint Version, string? Name, bool IsActive, string? FallbackCode) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public string Code { get; init; } = string.Empty;

    [JsonIgnore]
    public string Permission => PlatformPermissions.Localization.Manage;
}

/// <summary>Deletes a culture with its translations; the default culture cannot be deleted.</summary>
public sealed record DeleteCultureCommand(string Code) : ICommand, IRequirePermission
{
    public string Permission => PlatformPermissions.Localization.Manage;
}

/// <summary>Sets the translation of one key; an empty value removes it, so the embedded text applies again.</summary>
public sealed record SetTextCommand(string? Key, string? Value) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public string Culture { get; init; } = string.Empty;

    [JsonIgnore]
    public string Permission => PlatformPermissions.Localization.Manage;
}

/// <summary>
/// Imports translations from the Excel file of the export: column A the key, column C the text in the culture whose
/// code heads column C. Empty cells are skipped, unknown keys are reported.
/// </summary>
public sealed record ImportTextsCommand(string Culture, byte[] Content) : ICommand<ImportTextsResult>, IRequirePermission
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxRows = 20_000;

    public string Permission => PlatformPermissions.Localization.Manage;
}

/// <remarks>Skipped counts empty or too long texts; UnknownKeys lists at most 20 keys that are not in the catalog.</remarks>
public sealed record ImportTextsResult(int Imported, int Unchanged, int Skipped, IReadOnlyList<string> UnknownKeys);

public static class LocalizationAppErrors
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.localization.errors.tenant_scope_required");
    public const string UnknownKeyCode = "platform.localization.errors.unknown_key";
    public const string InvalidFileCode = "platform.localization.errors.invalid_file";
    public const string WrongCultureCode = "platform.localization.errors.wrong_culture";
    public const string UnknownCultureCode = "platform.localization.errors.unknown_culture";
}

public static class CultureCodes
{
    /// <summary>The canonical name of a specific culture (<c>en-us</c> → <c>en-US</c>); null when unknown or neutral.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > Culture.CodeMaxLength)
        {
            return null;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(code.Trim(), predefinedOnly: true);
            return culture.IsNeutralCulture || string.IsNullOrEmpty(culture.Name) ? null : culture.Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}

internal sealed class AddCultureCommandValidator : AbstractValidator<AddCultureCommand>
{
    public AddCultureCommandValidator()
    {
        RuleFor(c => c.Code).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(code => CultureCodes.Normalize(code) is not null).WithErrorCode(CommonErrors.InvalidCulture.Code);
        RuleFor(c => c.Name).MaximumLength(Culture.NameMaxLength);
    }
}

internal sealed class UpdateCultureCommandValidator : AbstractValidator<UpdateCultureCommand>
{
    public UpdateCultureCommandValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(Culture.NameMaxLength);
}

internal sealed class SetTextCommandValidator : AbstractValidator<SetTextCommand>
{
    public SetTextCommandValidator()
    {
        RuleFor(c => c.Key).NotEmpty().MaximumLength(CultureText.KeyMaxLength);
        RuleFor(c => c.Value).MaximumLength(CultureText.ValueMaxLength);
    }
}

internal sealed class ImportTextsCommandValidator : AbstractValidator<ImportTextsCommand>
{
    public ImportTextsCommandValidator() =>
        RuleFor(c => c.Content)
            .Must(content => content.Length is > 0 and <= ImportTextsCommand.MaxFileBytes)
            .WithErrorCode(LocalizationAppErrors.InvalidFileCode)
            .OverridePropertyName("file");
}

/// <summary>Shared checks of the localization commands.</summary>
internal static class LocalizationRules
{
    public static Task<bool> IsAllowedAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken) =>
        TenantAdministration.IsAllowedAsync(scopes, permission, cancellationToken);

    /// <summary>The fallback must be another existing culture of the tenant.</summary>
    public static async Task<Result<string?>> CheckFallbackAsync(IPlatformDbContext db, string? fallback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return Result.Success<string?>(null);
        }

        var code = CultureCodes.Normalize(fallback);
        return code is not null && await db.Cultures.AnyAsync(c => c.Code == code, cancellationToken)
            ? code
            : Error.Validation([new FieldError("fallbackCode", LocalizationAppErrors.UnknownCultureCode)]);
    }
}

internal sealed class AddCultureCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ILocalizationCache cache,
    IAfterCommit afterCommit) : ICommandHandler<AddCultureCommand, string>
{
    public async Task<Result<string>> Handle(AddCultureCommand request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        var code = CultureCodes.Normalize(request.Code)!;
        if (await db.Cultures.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Error.Validation([new FieldError("code", CommonErrors.Duplicate.Code)]);
        }

        var fallback = await LocalizationRules.CheckFallbackAsync(db, request.FallbackCode, cancellationToken);
        if (fallback.IsFailure)
        {
            return fallback.Error;
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? CultureInfo.GetCultureInfo(code).NativeName : request.Name;
        db.Cultures.Add(Culture.Create(code, name, fallback.Value));
        afterCommit.Enqueue(cache.InvalidateAsync);
        return code;
    }
}

internal sealed class UpdateCultureCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ILocalizationCache cache,
    IAfterCommit afterCommit) : ICommandHandler<UpdateCultureCommand>
{
    public async Task<Result> Handle(UpdateCultureCommand request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        var code = CultureCodes.Normalize(request.Code);
        var culture = code is null ? null : await db.Cultures.SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (culture is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(culture).ExpectVersion(request.Version);
        var fallback = await LocalizationRules.CheckFallbackAsync(db, request.FallbackCode, cancellationToken);
        if (fallback.IsFailure)
        {
            return fallback.Error;
        }

        var result = culture.Update(request.Name!, request.IsActive, fallback.Value);
        if (result.IsSuccess)
        {
            afterCommit.Enqueue(cache.InvalidateAsync);
        }

        return result;
    }
}

internal sealed class DeleteCultureCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ILocalizationCache cache,
    IAfterCommit afterCommit) : ICommandHandler<DeleteCultureCommand>
{
    public async Task<Result> Handle(DeleteCultureCommand request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        var code = CultureCodes.Normalize(request.Code);
        var culture = code is null ? null : await db.Cultures.Include(c => c.Texts).SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (culture is null)
        {
            return CommonErrors.NotFound;
        }

        var allowed = culture.CanDelete();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        db.Cultures.Remove(culture);
        afterCommit.Enqueue(cache.InvalidateAsync);
        return Result.Success();
    }
}

internal sealed class SetTextCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    IEmbeddedTexts embedded,
    ILocalizationCache cache,
    IAfterCommit afterCommit) : ICommandHandler<SetTextCommand>
{
    public async Task<Result> Handle(SetTextCommand request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        var key = request.Key!.Trim();
        if (embedded.Get(LocalizationSnapshot.EmbeddedCulture, key) is null)
        {
            return Error.Validation([new FieldError("key", LocalizationAppErrors.UnknownKeyCode)]);
        }

        var code = CultureCodes.Normalize(request.Culture);
        var culture = code is null
            ? null
            : await db.Cultures.Include(c => c.Texts.Where(t => t.Key == key)).SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (culture is null)
        {
            return CommonErrors.NotFound;
        }

        if (culture.SetText(key, request.Value))
        {
            afterCommit.Enqueue(cache.InvalidateAsync);
        }

        return Result.Success();
    }
}

internal sealed class ImportTextsCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    IEmbeddedTexts embedded,
    ISpreadsheet spreadsheet,
    ILocalizationCache cache,
    IAfterCommit afterCommit) : ICommandHandler<ImportTextsCommand, ImportTextsResult>
{
    private const int UnknownKeysShown = 20;

    public async Task<Result<ImportTextsResult>> Handle(ImportTextsCommand request, CancellationToken cancellationToken)
    {
        if (!await LocalizationRules.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return LocalizationAppErrors.TenantScopeRequired;
        }

        var code = CultureCodes.Normalize(request.Culture);
        var culture = code is null ? null : await db.Cultures.Include(c => c.Texts).SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (culture is null)
        {
            return CommonErrors.NotFound;
        }

        using var stream = new MemoryStream(request.Content, writable: false);
        var rows = spreadsheet.Read(stream, ImportTextsCommand.MaxRows + 1);
        if (rows is null || rows.Count == 0 || rows[0].Count < 3)
        {
            return Error.Validation([new FieldError("file", LocalizationAppErrors.InvalidFileCode)]);
        }

        // The column of the texts is headed by the culture code: a file exported for another culture is refused
        if (!string.Equals(rows[0][2]?.Trim(), culture.Code, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation([new FieldError("file", LocalizationAppErrors.WrongCultureCode, new Dictionary<string, object?> { ["culture"] = culture.Code })]);
        }

        int imported = 0, unchanged = 0, skipped = 0;
        var unknown = new List<string>();
        foreach (var row in rows.Skip(1))
        {
            var key = row[0]?.Trim();
            var value = row.Count > 2 ? row[2] : null;
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (embedded.Get(LocalizationSnapshot.EmbeddedCulture, key) is null || key.Length > CultureText.KeyMaxLength)
            {
                unknown.Add(key);
            }
            else if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > CultureText.ValueMaxLength)
            {
                skipped++;
            }
            else if (culture.SetText(key, value))
            {
                imported++;
            }
            else
            {
                unchanged++;
            }
        }

        if (imported > 0)
        {
            afterCommit.Enqueue(cache.InvalidateAsync);
        }

        return new ImportTextsResult(imported, unchanged, skipped, [.. unknown.Take(UnknownKeysShown)]);
    }
}
