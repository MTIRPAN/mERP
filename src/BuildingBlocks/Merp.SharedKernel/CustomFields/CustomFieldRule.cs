using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.CustomFields;

/// <summary>Data type of a tenant-defined field (D12).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomFieldType>))]
public enum CustomFieldType
{
    Text,
    Number,
    Date,
    Bool,
    Select,
    MultiSelect,
    User,
    Employee,
}

/// <summary>
/// What a value of a custom field must look like (D12, D30). <see cref="Normalize"/> turns an input into the stored
/// form: trimmed text, a number, <c>yyyy-MM-dd</c>, a boolean, option code(s) or a GUID.
/// </summary>
/// <param name="Type">Data type of the values.</param>
/// <param name="Required">A value must be given.</param>
/// <param name="Options">Option codes of Select and MultiSelect.</param>
/// <param name="MaxLength">Longest text; default <see cref="DefaultTextLength"/>.</param>
/// <param name="Min">Smallest number.</param>
/// <param name="Max">Largest number.</param>
/// <param name="WholeNumber">Only whole numbers.</param>
public sealed record CustomFieldRule(
    CustomFieldType Type,
    bool Required,
    IReadOnlyList<string> Options,
    int? MaxLength = null,
    decimal? Min = null,
    decimal? Max = null,
    bool WholeNumber = false)
{
    public const int DefaultTextLength = 500;
    public const int MaxTextLength = 4000;

    public const string InvalidValueCode = "common.errors.invalid_value";
    public const string MaxLengthCode = "common.errors.max_length";
    public const string BetweenCode = "common.errors.between";
    public const string MinCode = "common.errors.min_value";
    public const string MaxCode = "common.errors.max_value";
    public const string WholeNumberCode = "common.errors.integer";

    /// <summary>
    /// The stored value of an input, or null when there is none. A missing value of a required field is an error
    /// (<see cref="CommonErrors.Required"/>); other errors carry the arguments of their messages.
    /// </summary>
    public Result<JsonElement?> Normalize(JsonElement? input)
    {
        var value = input is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } given ? given : (JsonElement?)null;
        if (value is { ValueKind: JsonValueKind.String } text && string.IsNullOrWhiteSpace(text.GetString())
            || value is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() == 0)
        {
            value = null;
        }

        if (value is not { } present)
        {
            return Required ? CommonErrors.Required : Result.Success<JsonElement?>(null);
        }

        return Type switch
        {
            CustomFieldType.Text => NormalizeText(present),
            CustomFieldType.Number => NormalizeNumber(present),
            CustomFieldType.Date => present.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(present.GetString()!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? Of(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    : Invalid(),
            CustomFieldType.Bool => present.ValueKind is JsonValueKind.True or JsonValueKind.False ? Of(present.GetBoolean()) : Invalid(),
            CustomFieldType.Select => present.ValueKind == JsonValueKind.String && Options.Contains(present.GetString()!, StringComparer.Ordinal)
                ? Of(present.GetString()!)
                : Invalid(),
            CustomFieldType.MultiSelect => NormalizeOptions(present),
            CustomFieldType.User or CustomFieldType.Employee => present.ValueKind == JsonValueKind.String && Guid.TryParse(present.GetString(), out var id) && id != Guid.Empty
                ? Of(id.ToString())
                : Invalid(),
            _ => Invalid(),
        };
    }

    private Result<JsonElement?> NormalizeText(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return Invalid();
        }

        var text = value.GetString()!.Trim();
        var limit = MaxLength ?? DefaultTextLength;
        return text.Length > limit ? Error.Validation(MaxLengthCode, new Dictionary<string, object?> { ["maxLength"] = limit }) : Of(text);
    }

    private Result<JsonElement?> NormalizeNumber(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            return Invalid();
        }

        if (WholeNumber && decimal.Truncate(number) != number)
        {
            return Error.Validation(WholeNumberCode);
        }

        return (Min, Max) switch
        {
            ({ } min, { } max) when number < min || number > max => Error.Validation(BetweenCode, new Dictionary<string, object?> { ["from"] = Plain(min), ["to"] = Plain(max) }),
            ({ } min, null) when number < min => Error.Validation(MinCode, new Dictionary<string, object?> { ["min"] = Plain(min) }),
            (null, { } max) when number > max => Error.Validation(MaxCode, new Dictionary<string, object?> { ["max"] = Plain(max) }),
            _ => Of(number),
        };
    }

    private Result<JsonElement?> NormalizeOptions(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Invalid();
        }

        var codes = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Options.Contains(item.GetString()!, StringComparer.Ordinal))
            {
                return Invalid();
            }

            if (!codes.Contains(item.GetString()!, StringComparer.Ordinal))
            {
                codes.Add(item.GetString()!);
            }
        }

        return Of(codes);
    }

    // Limits read back from numeric columns keep their scale (30.0000); messages show 30
    private static decimal Plain(decimal value) => value / 1.0000000000000000000000000000m;

    private static Result<JsonElement?> Of<T>(T value) => Result.Success<JsonElement?>(JsonSerializer.SerializeToElement(value));

    private static Result<JsonElement?> Invalid() => Error.Validation(InvalidValueCode);
}
