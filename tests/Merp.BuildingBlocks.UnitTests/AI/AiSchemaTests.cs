using System.ComponentModel;
using System.Text.Json;
using Merp.Infrastructure.AI;

namespace Merp.BuildingBlocks.UnitTests.AI;

public sealed class AiSchemaTests
{
    private static readonly string[] UnsupportedKeywords = ["pattern", "minLength", "maxLength", "minimum", "maximum", "multipleOf", "maxItems"];

    [Fact]
    public void Schema_RequiresEveryProperty_AndForbidsAdditionalProperties()
    {
        var schema = Parse(typeof(SampleResult));

        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        Names(schema.GetProperty("required")).ShouldBe(["summary", "score", "years", "level", "items"]);

        var item = schema.GetProperty("properties").GetProperty("items").GetProperty("items");
        item.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        Names(item.GetProperty("required")).ShouldBe(["name", "note"]);
    }

    [Fact]
    public void Schema_MapsTypes_NullabilityEnumsAndDescriptions()
    {
        var properties = Parse(typeof(SampleResult)).GetProperty("properties");

        properties.GetProperty("summary").GetProperty("type").GetString().ShouldBe("string");
        properties.GetProperty("summary").GetProperty("description").GetString().ShouldBe("Short summary");
        properties.GetProperty("score").GetProperty("type").GetString().ShouldBe("integer");
        Names(properties.GetProperty("years").GetProperty("type")).ShouldBe(["number", "null"]);
        Names(properties.GetProperty("level").GetProperty("enum")).ShouldBe(["Low", "High"]);
        var note = properties.GetProperty("items").GetProperty("items").GetProperty("properties").GetProperty("note");
        Names(note.GetProperty("type")).ShouldBe(["string", "null"]);
    }

    [Fact]
    public void Schema_UsesOnlyKeywordsSupportedByStructuredOutputs()
    {
        var json = JsonSerializer.Serialize(AiSchema.For(typeof(SampleResult)));

        foreach (var keyword in UnsupportedKeywords)
        {
            json.ShouldNotContain($"\"{keyword}\"");
        }
    }

    [Fact]
    public void Output_RoundTrips_WithSerializerOptions()
    {
        var value = new SampleResult("ok", 80, 6.5m, SampleLevel.High, [new SampleItem("C#", null)]);

        var json = JsonSerializer.Serialize(value, AiSchema.SerializerOptions);

        json.ShouldContain("\"level\":\"High\"");
        var read = JsonSerializer.Deserialize<SampleResult>(json, AiSchema.SerializerOptions)!;
        read.ShouldBe(value with { Items = read.Items });
        read.Items.ShouldBe(value.Items);
    }

    private static JsonElement Parse(Type type) => JsonSerializer.SerializeToElement(AiSchema.For(type));

    private static string[] Names(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    public sealed record SampleResult(
        [property: Description("Short summary")] string Summary,
        int Score,
        decimal? Years,
        SampleLevel Level,
        IReadOnlyList<SampleItem> Items);

    public sealed record SampleItem(string Name, string? Note);

    public enum SampleLevel
    {
        Low,
        High,
    }
}
