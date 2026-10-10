using System.Text.Json;
using Merp.SharedKernel.CustomFields;
using Merp.SharedKernel.Results;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public sealed class CustomFieldRuleTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string? Stored(CustomFieldRule rule, string json) => rule.Normalize(Json(json)).Value?.GetRawText();

    [Fact]
    public void Required_FieldWithoutValue_IsAnError_EmptyTextAndListsCountAsMissing()
    {
        var rule = new CustomFieldRule(CustomFieldType.Text, Required: true, []);

        rule.Normalize(null).Error.ShouldBe(CommonErrors.Required);
        rule.Normalize(Json("\"  \"")).Error.ShouldBe(CommonErrors.Required);
        new CustomFieldRule(CustomFieldType.MultiSelect, Required: true, ["a"]).Normalize(Json("[]")).Error.ShouldBe(CommonErrors.Required);
        new CustomFieldRule(CustomFieldType.Text, Required: false, []).Normalize(Json("null")).Value.ShouldBeNull();
    }

    [Fact]
    public void Values_AreNormalized_PerType()
    {
        Stored(new(CustomFieldType.Text, false, []), "\"  Mavi  \"").ShouldBe("\"Mavi\"");
        Stored(new(CustomFieldType.Number, false, []), "42").ShouldBe("42");
        Stored(new(CustomFieldType.Date, false, []), "\"2026-02-28\"").ShouldBe("\"2026-02-28\"");
        Stored(new(CustomFieldType.Bool, false, []), "true").ShouldBe("true");
        Stored(new(CustomFieldType.Select, false, ["s", "m"]), "\"m\"").ShouldBe("\"m\"");
        Stored(new(CustomFieldType.MultiSelect, false, ["s", "m"]), "[\"m\",\"s\",\"m\"]").ShouldBe("[\"m\",\"s\"]");
        var id = Guid.NewGuid();
        Stored(new(CustomFieldType.User, false, []), $"\"{id.ToString().ToUpperInvariant()}\"").ShouldBe($"\"{id}\"");
    }

    [Theory]
    [InlineData(CustomFieldType.Number, "\"42\"")]
    [InlineData(CustomFieldType.Date, "\"28.02.2026\"")]
    [InlineData(CustomFieldType.Date, "\"2026-02-30\"")]
    [InlineData(CustomFieldType.Bool, "\"yes\"")]
    [InlineData(CustomFieldType.Select, "\"xl\"")]
    [InlineData(CustomFieldType.MultiSelect, "\"s\"")]
    [InlineData(CustomFieldType.MultiSelect, "[\"s\",\"xl\"]")]
    [InlineData(CustomFieldType.Employee, "\"not-a-guid\"")]
    [InlineData(CustomFieldType.Text, "42")]
    public void WrongValues_AreInvalid(CustomFieldType type, string json) =>
        new CustomFieldRule(type, false, ["s", "m"]).Normalize(Json(json)).Error.Code.ShouldBe(CustomFieldRule.InvalidValueCode);

    [Fact]
    public void Limits_CarryTheirArguments()
    {
        var text = new CustomFieldRule(CustomFieldType.Text, false, [], MaxLength: 3).Normalize(Json("\"abcd\"")).Error;
        (text.Code, text.Args["maxLength"]).ShouldBe((CustomFieldRule.MaxLengthCode, (object?)3));

        var number = new CustomFieldRule(CustomFieldType.Number, false, [], Min: 30, Max: 50, WholeNumber: true);
        number.Normalize(Json("29")).Error.Code.ShouldBe(CustomFieldRule.BetweenCode);
        // Limits read back from a numeric(18,4) column: the message shows 30, not 30.0000
        new CustomFieldRule(CustomFieldType.Number, false, [], Min: 30.0000m).Normalize(Json("29")).Error.Args["min"]!.ToString().ShouldBe("30");
        number.Normalize(Json("42.5")).Error.Code.ShouldBe(CustomFieldRule.WholeNumberCode);
        new CustomFieldRule(CustomFieldType.Number, false, [], Min: 1).Normalize(Json("0")).Error.Code.ShouldBe(CustomFieldRule.MinCode);
        new CustomFieldRule(CustomFieldType.Number, false, [], Max: 1).Normalize(Json("2")).Error.Code.ShouldBe(CustomFieldRule.MaxCode);
    }
}
