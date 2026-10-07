using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Domain.Localization;

public readonly record struct CultureId(Guid Value) : IEntityId<CultureId>
{
    public static CultureId From(Guid value) => new(value);
}

/// <summary>
/// A language of the tenant (D11) with the translations that override or add to the embedded texts. Texts are part
/// of the aggregate, so an Excel import is one change of one aggregate (rule 7).
/// </summary>
/// <remarks>
/// The code is a specific culture name (<c>en-US</c>) and never changes. The default culture is the last stop of
/// every fallback chain: it cannot be switched off or deleted.
/// </remarks>
[Audited]
public sealed class Culture : AuditableAggregateRoot<CultureId>
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 100;

    private readonly List<CultureText> _texts = [];

    private Culture()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public bool IsDefault { get; private set; }

    /// <summary>Culture whose texts stand in for missing ones; null uses the default culture.</summary>
    public string? FallbackCode { get; private set; }

    public IReadOnlyCollection<CultureText> Texts => _texts;

    public static Culture Create(string code, string name, string? fallbackCode, bool isDefault = false)
    {
        var culture = new Culture
        {
            Id = EntityId.New<CultureId>(),
            Code = Text.Required(code, CodeMaxLength, nameof(code)),
            IsActive = true,
            IsDefault = isDefault,
        };
        culture.Update(name, isActive: true, fallbackCode);
        return culture;
    }

    /// <summary>Renames the culture, switches it on or off and sets its fallback.</summary>
    public Result Update(string name, bool isActive, string? fallbackCode)
    {
        if (IsDefault && !isActive)
        {
            return LocalizationErrors.DefaultCultureRequired;
        }

        if (fallbackCode is not null && string.Equals(fallbackCode, Code, StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationErrors.SelfFallback;
        }

        Name = Text.Required(name, NameMaxLength, nameof(name));
        IsActive = isActive;
        FallbackCode = IsDefault ? null : fallbackCode;
        return Result.Success();
    }

    /// <summary>Sets the translation of a key, or removes it when <paramref name="value"/> is empty.</summary>
    /// <returns>True when something changed.</returns>
    public bool SetText(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var existing = _texts.Find(t => string.Equals(t.Key, key, StringComparison.Ordinal));
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        if (text is null)
        {
            return existing is not null && _texts.Remove(existing);
        }

        if (text.Length > CultureText.ValueMaxLength)
        {
            throw new ArgumentException($"A text has at most {CultureText.ValueMaxLength} characters.", nameof(value));
        }

        if (existing is null)
        {
            _texts.Add(new CultureText(Id, key, text));
            return true;
        }

        return existing.Change(text);
    }

    /// <summary>The culture may be deleted unless it is the default.</summary>
    public Result CanDelete() => IsDefault ? LocalizationErrors.DefaultCultureRequired : Result.Success();
}

/// <summary>A translation of one key in one culture. Audited as its own row, so every translation change is logged.</summary>
[Audited]
public sealed class CultureText
{
    public const int KeyMaxLength = 200;
    public const int ValueMaxLength = 4000;

    private CultureText()
    {
    }

    internal CultureText(CultureId cultureId, string key, string value)
    {
        CultureId = cultureId;
        Key = Text.Required(key, KeyMaxLength, nameof(key));
        Value = value;
    }

    public CultureId CultureId { get; private set; }

    public string Key { get; private set; } = null!;

    public string Value { get; private set; } = null!;

    internal bool Change(string value)
    {
        if (string.Equals(Value, value, StringComparison.Ordinal))
        {
            return false;
        }

        Value = value;
        return true;
    }
}

public static class LocalizationErrors
{
    /// <summary>The default culture must stay active and cannot be deleted.</summary>
    public static readonly Error DefaultCultureRequired = Error.Conflict("platform.localization.errors.default_culture_required");

    public static readonly Error SelfFallback = Error.Validation([new FieldError("fallbackCode", "platform.localization.errors.self_fallback")]);
}
