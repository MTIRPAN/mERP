using System.Collections.ObjectModel;
using System.Text.Json;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Values of tenant-defined custom fields, stored in the <c>custom_fields</c> jsonb column (D12).
/// Keys are field codes; validation against the definitions happens in the application layer.
/// </summary>
public sealed class CustomFieldValues
{
    public static readonly CustomFieldValues Empty = new(new Dictionary<string, JsonElement>());

    private CustomFieldValues(IDictionary<string, JsonElement> values) =>
        Values = new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(values, StringComparer.Ordinal));

    public IReadOnlyDictionary<string, JsonElement> Values { get; }

    public static CustomFieldValues From(IReadOnlyDictionary<string, JsonElement>? values) =>
        values is null || values.Count == 0
            ? Empty
            : new CustomFieldValues(values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal));
}
