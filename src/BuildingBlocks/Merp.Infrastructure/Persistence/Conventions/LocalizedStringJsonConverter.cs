using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Infrastructure.Persistence.Conventions;

/// <summary>
/// Writes a <see cref="LocalizedString"/> inside a jsonb value as <c>{"tr-TR": "...", "en-US": "..."}</c>, the shape of
/// its own columns (D11), e.g. the stage names of a recruitment stage template.
/// </summary>
public sealed class LocalizedStringJsonConverter : JsonConverter<LocalizedString>
{
    public override LocalizedString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        LocalizedString.Create(JsonSerializer.Deserialize<Dictionary<string, string?>>(ref reader, options)).Value;

    public override void Write(Utf8JsonWriter writer, LocalizedString value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        JsonSerializer.Serialize(writer, value.Values, options);
    }
}
