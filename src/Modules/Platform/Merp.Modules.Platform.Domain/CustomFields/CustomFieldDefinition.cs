using System.Text.RegularExpressions;
using Merp.SharedKernel.CustomFields;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.CustomFields;

public readonly record struct CustomFieldDefinitionId(Guid Value) : IEntityId<CustomFieldDefinitionId>
{
    public static CustomFieldDefinitionId From(Guid value) => new(value);
}

/// <summary>An option of a select field; the code is stored in the records, the label is shown.</summary>
public sealed record CustomFieldOption(string Code, IReadOnlyDictionary<string, string> Label);

/// <summary>The settings of a definition that may change after creation.</summary>
public sealed record CustomFieldSettings(
    LocalizedString Label,
    bool Required,
    IReadOnlyList<CustomFieldOption> Options,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    bool WholeNumber,
    LocalizedString? Group,
    int Order,
    bool IsActive);

/// <summary>
/// A tenant-defined field of an entity type (PLAN P6, D12, D30). The entity type, code, data type and company never
/// change: stored values depend on them. A field is switched off instead of deleted, so stored values survive.
/// </summary>
[Audited]
public sealed partial class CustomFieldDefinition : AuditableAggregateRoot<CustomFieldDefinitionId>
{
    public const int EntityTypeMaxLength = 100;
    public const int CodeMaxLength = 50;
    public const int MaxOptions = 200;

    public const string InvalidCodeCode = "platform.custom_fields.errors.invalid_code";
    public const string OptionsRequiredCode = "platform.custom_fields.errors.options_required";
    public const string InvalidOptionCode = "platform.custom_fields.errors.invalid_option";
    public const string NotForTypeCode = "platform.custom_fields.errors.not_for_type";

    private CustomFieldDefinition()
    {
    }

    public string EntityType { get; private set; } = null!;

    /// <summary>Key of the value in the records' <c>custom_fields</c>: lower case letters, digits and underscores.</summary>
    public string Code { get; private set; } = null!;

    public CustomFieldType Type { get; private set; }

    /// <summary>Null for every company; otherwise only the records of this company have the field.</summary>
    public Guid? CompanyId { get; private set; }

    public LocalizedString Label { get; private set; } = null!;

    public bool Required { get; private set; }

    public IReadOnlyList<CustomFieldOption> Options { get; private set; } = [];

    public int? MaxLength { get; private set; }

    public decimal? Min { get; private set; }

    public decimal? Max { get; private set; }

    public bool WholeNumber { get; private set; }

    public LocalizedString? Group { get; private set; }

    public int Order { get; private set; }

    public bool IsActive { get; private set; }

    public CustomFieldRule Rule => new(Type, Required, [.. Options.Select(o => o.Code)], MaxLength, Min, Max, WholeNumber);

    public static bool IsValidCode(string? code) => code is not null && CodePattern().IsMatch(code);

    public static Result<CustomFieldDefinition> Create(string entityType, string code, CustomFieldType type, Guid? companyId, CustomFieldSettings settings)
    {
        if (!IsValidCode(code))
        {
            return Error.Validation([new FieldError("code", InvalidCodeCode)]);
        }

        var definition = new CustomFieldDefinition
        {
            Id = EntityId.New<CustomFieldDefinitionId>(),
            EntityType = Text.Required(entityType, EntityTypeMaxLength, nameof(entityType)),
            Code = code,
            Type = type,
            CompanyId = companyId,
        };
        var updated = definition.Update(settings);
        return updated.IsSuccess ? definition : updated.Error;
    }

    public Result Update(CustomFieldSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<FieldError>();
        var choice = Type is CustomFieldType.Select or CustomFieldType.MultiSelect;

        if (choice && settings.Options.Count == 0)
        {
            errors.Add(new FieldError("options", OptionsRequiredCode));
        }
        else if (!choice && settings.Options.Count > 0)
        {
            errors.Add(new FieldError("options", NotForTypeCode));
        }
        else if (settings.Options.Count > MaxOptions
                 || settings.Options.Any(o => !IsValidCode(o.Code) || o.Label.Count == 0)
                 || settings.Options.Select(o => o.Code).Distinct(StringComparer.Ordinal).Count() != settings.Options.Count)
        {
            errors.Add(new FieldError("options", InvalidOptionCode));
        }

        if (settings.MaxLength is { } maxLength && (Type != CustomFieldType.Text || maxLength is < 1 or > CustomFieldRule.MaxTextLength))
        {
            errors.Add(new FieldError("maxLength", Type == CustomFieldType.Text ? CustomFieldRule.InvalidValueCode : NotForTypeCode));
        }

        if ((settings.Min is not null || settings.Max is not null || settings.WholeNumber) && Type != CustomFieldType.Number)
        {
            errors.Add(new FieldError("min", NotForTypeCode));
        }
        else if (settings is { Min: { } min, Max: { } max } && min > max)
        {
            errors.Add(new FieldError("max", CustomFieldRule.InvalidValueCode));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        Label = settings.Label;
        Required = settings.Required;
        Options = settings.Options;
        MaxLength = settings.MaxLength;
        Min = settings.Min;
        Max = settings.Max;
        WholeNumber = settings.WholeNumber;
        Group = settings.Group;
        Order = settings.Order;
        IsActive = settings.IsActive;
        return Result.Success();
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
