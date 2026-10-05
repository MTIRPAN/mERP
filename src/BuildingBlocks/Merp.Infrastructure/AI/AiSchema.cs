using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Merp.Infrastructure.AI;

/// <summary>
/// JSON schema and serializer settings for AI result types. The schema follows the structured outputs subset: every
/// property is required, objects allow no additional properties, <see cref="DescriptionAttribute"/> texts become
/// descriptions (they guide the model), and nullable members become <c>["type", "null"]</c>.
/// </summary>
public static class AiSchema
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, JsonElement>> Cache = new();

    /// <summary>
    /// camelCase, string enums, nullable annotations respected; used for both schema and deserialization. Numbers are
    /// strict so the schema has no string alternatives or patterns, which structured outputs do not support.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyDictionary<string, JsonElement> For(Type type) =>
        Cache.GetOrAdd(type, static t => Build(t));

    private static Dictionary<string, JsonElement> Build(Type type)
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Transform,
        };

        var node = SerializerOptions.GetJsonSchemaAsNode(type, exporterOptions);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(node.ToJsonString())
            ?? throw new InvalidOperationException($"Could not build a JSON schema for {type}.");
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (schema is not JsonObject obj)
        {
            return schema;
        }

        ICustomAttributeProvider attributes = context.PropertyInfo is { } property
            ? property.AttributeProvider ?? context.TypeInfo.Type
            : context.TypeInfo.Type;
        var description = attributes
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;
        if (description is not null)
        {
            obj["description"] = description;
        }

        if (obj["properties"] is JsonObject properties)
        {
            obj["required"] = new JsonArray([.. properties.Select(p => (JsonNode)JsonValue.Create(p.Key))]);
            obj["additionalProperties"] = false;
        }

        return obj;
    }
}
