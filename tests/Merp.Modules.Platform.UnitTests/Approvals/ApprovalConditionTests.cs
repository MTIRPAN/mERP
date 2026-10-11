using System.Text.Json;
using Merp.Application.Abstractions.Approvals;
using Merp.Modules.Platform.Application.Approvals;

namespace Merp.Modules.Platform.UnitTests.Approvals;

/// <summary>The condition language of approval flows (docs/domain/yetki-ve-onay.md §2.3).</summary>
public sealed class ApprovalConditionTests
{
    private static readonly ApprovalFact[] Facts =
    [
        new("leaveTypeCode", ApprovalFactType.Text, "f"),
        new("days", ApprovalFactType.Number, "f"),
        new("isPeakSeason", ApprovalFactType.Boolean, "f"),
        new("startDate", ApprovalFactType.Date, "f"),
        new("orgUnitId", ApprovalFactType.Identifier, "f"),
        new("tags", ApprovalFactType.TextList, "f"),
    ];

    private const string SpecExample = """
        { "all": [
          { "fact": "leaveTypeCode", "op": "in", "value": ["YILLIK", "UCRETSIZ"] },
          { "any": [
            { "fact": "days", "op": "gt", "value": 5 },
            { "fact": "isPeakSeason", "op": "eq", "value": true }
          ]}
        ]}
        """;

    [Theory]
    [InlineData("""{ "leaveTypeCode": "YILLIK", "days": 7, "isPeakSeason": false }""", true)]
    [InlineData("""{ "leaveTypeCode": "UCRETSIZ", "days": 2, "isPeakSeason": true }""", true)]
    [InlineData("""{ "leaveTypeCode": "YILLIK", "days": 5, "isPeakSeason": false }""", false)]
    [InlineData("""{ "leaveTypeCode": "MAZERET", "days": 10, "isPeakSeason": true }""", false)]
    [InlineData("""{ "days": 10 }""", false)]
    public void SpecExample_IsEvaluated(string facts, bool expected) => Evaluate(SpecExample, facts).ShouldBe(expected);

    [Theory]
    [InlineData("""{ "fact": "days", "op": "gte", "value": 5 }""", """{ "days": 5 }""", true)]
    [InlineData("""{ "fact": "days", "op": "lt", "value": 5 }""", """{ "days": 4.5 }""", true)]
    [InlineData("""{ "fact": "days", "op": "lte", "value": 5 }""", """{ "days": 6 }""", false)]
    [InlineData("""{ "fact": "days", "op": "eq", "value": 5 }""", """{ "days": 5.0 }""", true)]
    [InlineData("""{ "fact": "days", "op": "ne", "value": 5 }""", """{ }""", true)]
    [InlineData("""{ "fact": "startDate", "op": "gt", "value": "2026-06-30" }""", """{ "startDate": "2026-07-01" }""", true)]
    [InlineData("""{ "fact": "startDate", "op": "lt", "value": "2026-06-30" }""", """{ "startDate": "2026-07-01" }""", false)]
    [InlineData("""{ "fact": "leaveTypeCode", "op": "nin", "value": ["YILLIK"] }""", """{ "leaveTypeCode": "MAZERET" }""", true)]
    [InlineData("""{ "fact": "leaveTypeCode", "op": "nin", "value": ["YILLIK"] }""", """{ }""", true)]
    [InlineData("""{ "fact": "leaveTypeCode", "op": "contains", "value": "yil" }""", """{ "leaveTypeCode": "YILLIK" }""", true)]
    [InlineData("""{ "fact": "tags", "op": "contains", "value": "acil" }""", """{ "tags": ["acil", "yurtdisi"] }""", true)]
    [InlineData("""{ "fact": "tags", "op": "contains", "value": "Acil" }""", """{ "tags": ["acil"] }""", false)]
    [InlineData("""{ "fact": "tags", "op": "isEmpty" }""", """{ "tags": [] }""", true)]
    [InlineData("""{ "fact": "leaveTypeCode", "op": "isEmpty" }""", """{ "leaveTypeCode": " " }""", true)]
    [InlineData("""{ "fact": "leaveTypeCode", "op": "isNotEmpty" }""", """{ "leaveTypeCode": null }""", false)]
    [InlineData("""{ "fact": "orgUnitId", "op": "eq", "value": "0199b6a0-0000-7000-8000-000000000001" }""", """{ "orgUnitId": "0199B6A0-0000-7000-8000-000000000001" }""", true)]
    [InlineData("""{ "fact": "isPeakSeason", "op": "eq", "value": false }""", """{ "isPeakSeason": true }""", false)]
    public void Operators_AreEvaluated(string condition, string facts, bool expected) => Evaluate(condition, facts).ShouldBe(expected);

    [Fact]
    public void NoCondition_AlwaysMatches()
    {
        ApprovalConditions.Parse(null, Facts, "condition").Value.ShouldBeNull();
        ApprovalConditions.Evaluate(null, JsonDocument.Parse("{}").RootElement).ShouldBeTrue();
    }

    [Theory]
    [InlineData("not json", ApprovalConditions.InvalidCode)]
    [InlineData("""[]""", ApprovalConditions.InvalidCode)]
    [InlineData("""{ "all": [] }""", ApprovalConditions.InvalidCode)]
    [InlineData("""{ "all": [ { "fact": "days", "op": "gt", "value": 1 } ], "fact": "days" }""", ApprovalConditions.InvalidCode)]
    [InlineData("""{ "fact": "days" }""", ApprovalConditions.InvalidCode)]
    [InlineData("""{ "fact": "salary", "op": "gt", "value": 1 }""", ApprovalConditions.UnknownFactCode)]
    [InlineData("""{ "fact": "days", "op": "like", "value": 1 }""", ApprovalConditions.InvalidOperatorCode)]
    [InlineData("""{ "fact": "leaveTypeCode", "op": "gt", "value": "A" }""", ApprovalConditions.InvalidOperatorCode)]
    [InlineData("""{ "fact": "tags", "op": "in", "value": ["A"] }""", ApprovalConditions.InvalidOperatorCode)]
    [InlineData("""{ "fact": "days", "op": "gt", "value": "5" }""", ApprovalConditions.InvalidValueCode)]
    [InlineData("""{ "fact": "days", "op": "in", "value": [] }""", ApprovalConditions.InvalidValueCode)]
    [InlineData("""{ "fact": "startDate", "op": "gt", "value": "01.07.2026" }""", ApprovalConditions.InvalidValueCode)]
    [InlineData("""{ "fact": "orgUnitId", "op": "eq", "value": "x" }""", ApprovalConditions.InvalidValueCode)]
    [InlineData("""{ "fact": "days", "op": "isEmpty", "value": 1 }""", ApprovalConditions.InvalidValueCode)]
    public void InvalidConditions_AreRejected(string condition, string code)
    {
        var result = ApprovalConditions.Parse(condition, Facts, "steps[0].condition");

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields.Single().Field.ShouldBe("steps[0].condition");
        result.Error.Fields.Single().Code.ShouldBe(code);
    }

    [Fact]
    public void DeepTrees_AreRejected()
    {
        var condition = """{ "fact": "days", "op": "gt", "value": 1 }""";
        for (var i = 0; i < 12; i++)
        {
            condition = $$"""{ "all": [ {{condition}} ] }""";
        }

        ApprovalConditions.Parse(condition, Facts, "condition").Error.Fields.Single().Code.ShouldBe(ApprovalConditions.InvalidCode);
    }

    private static bool Evaluate(string condition, string facts)
    {
        var parsed = ApprovalConditions.Parse(condition, Facts, "condition");
        parsed.IsSuccess.ShouldBeTrue(parsed.IsFailure ? parsed.Error.Fields.Single().Code : null);
        return ApprovalConditions.Evaluate(parsed.Value, JsonDocument.Parse(facts).RootElement);
    }
}
