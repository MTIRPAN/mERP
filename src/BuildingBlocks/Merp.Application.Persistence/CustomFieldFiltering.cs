using System.Text.Json;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Application.Persistence;

/// <summary>
/// List filter on custom fields (D12, D30): the record's values must contain the given ones (jsonb <c>@&gt;</c>, served
/// by the GIN index of <c>custom_fields</c>). A multi-select value matches when it holds the given options.
/// </summary>
public static class CustomFieldFiltering
{
    public const string InvalidFilterCode = "common.errors.invalid_custom_field_filter";

    /// <summary>Parses the <c>customFields</c> query parameter: a JSON object of field code and value.</summary>
    /// <returns>Null when absent; false in <c>valid</c> when it is not a JSON object.</returns>
    public static CustomFieldValues? Parse(string? json, out bool valid)
    {
        valid = true;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            return values is null || values.Count == 0 ? null : CustomFieldValues.From(values);
        }
        catch (JsonException)
        {
            valid = false;
            return null;
        }
    }

    public static IQueryable<T> WhereCustomFields<T>(this IQueryable<T> query, CustomFieldValues? filter)
        where T : class, IHasCustomFields
    {
        if (filter is null)
        {
            return query;
        }

        // A JsonElement parameter, which the provider sends as jsonb
        var json = JsonSerializer.SerializeToElement(filter.Values);
        return query.Where(e => Contain(e.CustomFields, json));
    }

    /// <summary>
    /// Only for LINQ queries: Infrastructure rewrites it to the provider's jsonb containment before translation,
    /// so the application layer does not depend on the database provider.
    /// </summary>
    public static bool Contain(CustomFieldValues values, JsonElement filter) =>
        throw new InvalidOperationException("CustomFieldFiltering.Contain is translated to SQL and cannot run in memory.");
}
