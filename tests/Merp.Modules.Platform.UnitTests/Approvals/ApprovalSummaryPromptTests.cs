using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Localization;
using Merp.Modules.Platform.Application.Approvals;
using NSubstitute;

namespace Merp.Modules.Platform.UnitTests.Approvals;

/// <summary>The facts an approval summary sends (PLAN A8, D61): the declared ones with their labels, never identifiers.</summary>
public sealed class ApprovalSummaryPromptTests
{
    private static readonly ApprovalDocumentType Offer = new(
        "test.offer",
        "test.offer.name",
        [
            new ApprovalFact("salary", ApprovalFactType.Number, "test.offer.salary"),
            new ApprovalFact("isAboveBudget", ApprovalFactType.Boolean, "test.offer.above_budget"),
            new ApprovalFact("leaveTypes", ApprovalFactType.TextList, "test.offer.types"),
            new ApprovalFact("hiringManagerUserId", ApprovalFactType.Identifier, "test.offer.manager"),
            new ApprovalFact("grade", ApprovalFactType.Text, "test.offer.grade"),
        ]);

    [Fact]
    public void TheFacts_KeepTheDeclaredValuesWithTheirLabels_AndLeaveIdentifiersOut()
    {
        var localizer = Substitute.For<ITextLocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string>(0) switch
            {
                "test.offer.salary" => "Aylık ücret",
                "test.offer.above_budget" => "Bütçe üstü",
                "test.offer.types" => "Türler",
                _ => call.ArgAt<string>(0),
            });

        var facts = ApprovalSummaryPrompt.Facts(
            Offer,
            """{ "salary": 95000, "isAboveBudget": true, "leaveTypes": ["YILLIK", "MAZERET"], "hiringManagerUserId": "0199f0d4-0000-7000-8000-000000000001", "grade": null, "extra": 1 }""",
            localizer);

        facts.ShouldBe(
        [
            ("Aylık ücret", ApprovalFactType.Number, "95000"),
            ("Bütçe üstü", ApprovalFactType.Boolean, "true"),
            ("Türler", ApprovalFactType.TextList, "YILLIK, MAZERET"),
        ]);
        ApprovalSummaryPrompt.Facts(null, """{ "salary": 1 }""", localizer).ShouldBeEmpty();
    }
}
