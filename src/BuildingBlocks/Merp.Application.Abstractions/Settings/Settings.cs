using System.Text.Json;
using System.Text.Json.Serialization;

namespace Merp.Application.Abstractions.Settings;

/// <summary>Value type of a setting (D35).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SettingType>))]
public enum SettingType
{
    Boolean,

    /// <summary>A decimal, or a whole number with <see cref="SettingDefinition.WholeNumber"/>.</summary>
    Number,

    Text,

    /// <summary>One of <see cref="SettingDefinition.Options"/>, stored as the option's value.</summary>
    Choice,

    /// <summary>A calendar day, stored as <c>yyyy-MM-dd</c>; empty until set (read as a nullable <see cref="DateOnly"/>).</summary>
    Date,
}

/// <param name="Value">Stored value, e.g. <c>immediately</c>.</param>
/// <param name="LabelKey">Localization key of the option's label.</param>
public sealed record SettingOption(string Value, string LabelKey);

/// <summary>
/// A setting a module declares (D35), e.g. <c>platform.notifications.email_enabled</c>. The first segment is the
/// module; the settings screen groups by it. Values are set for the tenant and, where
/// <see cref="CompanyOverridable"/>, for a company; reading falls back company → tenant → <see cref="DefaultValue"/>.
/// </summary>
/// <param name="Key">Setting key, <c>{module}.{group}.{name}</c>.</param>
/// <param name="Type">Value type.</param>
/// <param name="DefaultValue">Value when neither the tenant nor the company set one.</param>
/// <param name="LabelKey">Localization key of the name on the settings screen.</param>
/// <param name="DescriptionKey">Localization key of the explanation under the name.</param>
/// <param name="CompanyOverridable">A company may have its own value.</param>
/// <param name="Options">Choices of a <see cref="SettingType.Choice"/> setting.</param>
/// <param name="Min">Smallest number allowed.</param>
/// <param name="Max">Largest number allowed.</param>
/// <param name="WholeNumber">Numbers without decimals.</param>
/// <param name="MaxLength">Longest text allowed.</param>
/// <param name="Order">Position within the module.</param>
public sealed record SettingDefinition(
    string Key,
    SettingType Type,
    JsonElement DefaultValue,
    string LabelKey,
    string? DescriptionKey = null,
    bool CompanyOverridable = false,
    IReadOnlyList<SettingOption>? Options = null,
    decimal? Min = null,
    decimal? Max = null,
    bool WholeNumber = false,
    int? MaxLength = null,
    int Order = 0)
{
    public string Module => Key.Split('.')[0];

    public static SettingDefinition Boolean(string key, bool defaultValue, string labelKey, string? descriptionKey = null, bool companyOverridable = false, int order = 0) =>
        new(key, SettingType.Boolean, JsonSerializer.SerializeToElement(defaultValue), labelKey, descriptionKey, companyOverridable, Order: order);

    public static SettingDefinition Number(
        string key,
        decimal defaultValue,
        string labelKey,
        string? descriptionKey = null,
        bool companyOverridable = false,
        decimal? min = null,
        decimal? max = null,
        bool wholeNumber = false,
        int order = 0) =>
        new(key, SettingType.Number, JsonSerializer.SerializeToElement(defaultValue), labelKey, descriptionKey, companyOverridable, Min: min, Max: max, WholeNumber: wholeNumber, Order: order);

    public static SettingDefinition Text(string key, string defaultValue, string labelKey, string? descriptionKey = null, bool companyOverridable = false, int maxLength = 500, int order = 0) =>
        new(key, SettingType.Text, JsonSerializer.SerializeToElement(defaultValue), labelKey, descriptionKey, companyOverridable, MaxLength: maxLength, Order: order);

    public static SettingDefinition Choice(
        string key,
        string defaultValue,
        IReadOnlyList<SettingOption> options,
        string labelKey,
        string? descriptionKey = null,
        bool companyOverridable = false,
        int order = 0) =>
        new(key, SettingType.Choice, JsonSerializer.SerializeToElement(defaultValue), labelKey, descriptionKey, companyOverridable, options, Order: order);
}

/// <summary>Factories of date settings, which have no default day.</summary>
public static class DateSettings
{
    public static SettingDefinition Create(string key, string labelKey, string? descriptionKey = null, bool companyOverridable = false, int order = 0) =>
        new(key, SettingType.Date, JsonSerializer.SerializeToElement<DateOnly?>(null), labelKey, descriptionKey, companyOverridable, Order: order);
}

/// <summary>Modules declare their settings, like their permissions.</summary>
public interface ISettingDefinitionProvider
{
    IEnumerable<SettingDefinition> GetSettings();
}

/// <summary>The settings of all modules.</summary>
public sealed class SettingDefinitionRegistry(IEnumerable<ISettingDefinitionProvider> providers)
{
    private readonly Dictionary<string, SettingDefinition> _settings =
        providers.SelectMany(p => p.GetSettings()).ToDictionary(s => s.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<SettingDefinition> All => _settings.Values;

    public SettingDefinition? Find(string? key) => key is not null && _settings.TryGetValue(key, out var setting) ? setting : null;

    /// <summary>The definition of a key a module reads; an unknown key is a programming error.</summary>
    public SettingDefinition Get(string key) =>
        Find(key) ?? throw new InvalidOperationException($"Setting {key} is not declared by any module.");
}

/// <summary>
/// Reads settings (D35): the company's value, otherwise the tenant's, otherwise the default. Platform keeps the values;
/// without it every setting has its default.
/// </summary>
public interface ISettings
{
    /// <typeparam name="T"><c>bool</c>, <c>decimal</c>/<c>int</c>, <c>string</c> or <c>DateOnly?</c>, matching the setting's type.</typeparam>
    Task<T> GetAsync<T>(string key, Guid? companyId = null, CancellationToken cancellationToken = default);
}
