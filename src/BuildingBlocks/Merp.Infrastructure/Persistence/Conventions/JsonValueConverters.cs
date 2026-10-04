using System.Text.Encodings.Web;
using System.Text.Json;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Merp.Infrastructure.Persistence.Conventions;

/// <summary>Stores <see cref="LocalizedString"/> as jsonb <c>{"tr-TR": "...", "en-US": "..."}</c> (D11).</summary>
public sealed class LocalizedStringConverter() : ValueConverter<LocalizedString, string>(
    value => JsonSerializer.Serialize(value.Values, JsonOptions.Default),
    json => LocalizedString.Create(JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions.Default)).Value);

public sealed class LocalizedStringComparer() : ValueComparer<LocalizedString>(
    (left, right) => ReferenceEquals(left, right) || (left != null && left.Equals(right)),
    value => value.GetHashCode(),
    value => value);

/// <summary>Stores <see cref="CustomFieldValues"/> as jsonb (D12).</summary>
public sealed class CustomFieldValuesConverter() : ValueConverter<CustomFieldValues, string>(
    value => JsonSerializer.Serialize(value.Values, JsonOptions.Default),
    json => CustomFieldValues.From(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions.Default)));

public sealed class CustomFieldValuesComparer() : ValueComparer<CustomFieldValues>(
    (left, right) => JsonSerializer.Serialize(left!.Values, JsonOptions.Default) == JsonSerializer.Serialize(right!.Values, JsonOptions.Default),
    value => JsonSerializer.Serialize(value.Values, JsonOptions.Default).GetHashCode(StringComparison.Ordinal),
    value => CustomFieldValues.From(value.Values));

internal static class JsonOptions
{
    // Stored in the database, never rendered as HTML: keep non-ASCII text (ç, ğ, ı, ş...) readable.
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
