using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Infrastructure.Persistence.Conventions;

/// <summary>Values stored as a jsonb column of their row, e.g. a list of small records, in every module.</summary>
public static class JsonColumns
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Stored in the database, never rendered as HTML: keep non-ASCII text (ç, ğ, ı, ş...) readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(), new LocalizedStringJsonConverter() },
    };

    /// <summary>Stores a value as jsonb; changes are detected by comparing the JSON.</summary>
    public static PropertyBuilder<T> HasJson<T>(this PropertyBuilder<T> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .HasConversion(
                value => JsonSerializer.Serialize(value, Options),
                json => JsonSerializer.Deserialize<T>(json, Options)!,
                new ValueComparer<T>(
                    (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
                    value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
                    value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!))
            .HasColumnType("jsonb");
    }
}
