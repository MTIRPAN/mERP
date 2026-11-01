using Merp.Application.Abstractions.AI;
using Merp.Modules.Platform.Application.Assistant;

namespace Merp.Modules.Platform.UnitTests.Assistant;

/// <summary>What the assistant sends and which of the model's arguments reach an action (PLAN A12, D65).</summary>
public sealed class AssistantPromptTests
{
    private static readonly AssistantActionDefinition Requests = new(
        "hr.my_leave_requests",
        "The user's own leave requests.",
        "hr.self.leave.read",
        [
            new("status", AssistantParameterType.Choice, "Only these states.", ["Draft", "Pending", "Approved"]),
            new("search", AssistantParameterType.Text, "Words to look for."),
        ]);

    [Fact]
    public void TheInput_GivesTheActionsThePagesAndTheQuestion()
    {
        var input = AssistantPrompt.BuildInput(
            [Requests],
            [new AssistantPage("hr-my-leave", "İzinlerim", "İnsan Kaynakları", "/hr/me/leave"), new AssistantPage("profile", "Profilim", null, "/profile")],
            "Onay bekleyen izinlerim",
            "tr-TR");

        input.ShouldSatisfyAllConditions(
            i => i.ShouldStartWith("Output language: Turkish"),
            i => i.ShouldContain("<action key=\"hr.my_leave_requests\">The user's own leave requests.\n<parameter name=\"status\" type=\"Choice\" values=\"Draft, Pending, Approved\">Only these states.</parameter>\n<parameter name=\"search\" type=\"Text\">Words to look for.</parameter>\n</action>"),
            i => i.ShouldContain("<page key=\"hr-my-leave\" section=\"İnsan Kaynakları\">İzinlerim</page>\n<page key=\"profile\">Profilim</page>"),
            i => i.ShouldEndWith("<question>\nOnay bekleyen izinlerim\n</question>"));
    }

    [Fact]
    public void OnlyDeclaredArguments_WithValidValues_ReachTheAction()
    {
        var arguments = AssistantArgumentCheck.Check(Requests, [
            new AssistantPlanArgument(" STATUS ", "pending, Unknown,Approved, Pending"),
            new AssistantPlanArgument("search", "  " + new string('a', 150) + "  "),
            new AssistantPlanArgument("company", "Acme"),
        ]);

        arguments.Text("status").ShouldBe("Pending,Approved");
        arguments.Text("search").ShouldBe(new string('a', AssistantArguments.TextMaxLength));
        arguments.Text("company").ShouldBeNull();
        arguments.Choices<Kind>("status").ShouldBe([Kind.Pending, Kind.Approved]);
    }

    [Fact]
    public void EmptyValues_NoValidChoice_AndRedactionPlaceholders_AreLeftOut()
    {
        var arguments = AssistantArgumentCheck.Check(Requests, [
            new AssistantPlanArgument("status", "Unknown"),
            new AssistantPlanArgument("search", "[NATIONAL_ID]"),
        ]);

        arguments.Text("status").ShouldBeNull();
        arguments.Text("search").ShouldBeNull();
        arguments.Choices<Kind>("status").ShouldBeEmpty();
        AssistantArgumentCheck.Check(Requests, [new AssistantPlanArgument("search", "  ")]).Text("search").ShouldBeNull();
    }

    private enum Kind
    {
        Draft,
        Pending,
        Approved,
    }
}
