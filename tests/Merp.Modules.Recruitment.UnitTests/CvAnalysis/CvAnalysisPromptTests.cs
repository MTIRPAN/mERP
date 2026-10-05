using System.Text.Json;
using Merp.Infrastructure.AI;
using Merp.Modules.Recruitment.Application.CvAnalysis;

namespace Merp.Modules.Recruitment.UnitTests.CvAnalysis;

public sealed class CvAnalysisPromptTests
{
    [Fact]
    public void Input_WithoutRequirements_AsksForNoMatch()
    {
        var input = CvAnalysisPrompt.BuildInput("CV text", "Backend Developer", null, "tr-TR");

        input.ShouldStartWith("Output language: Turkish");
        input.ShouldContain("Set match to null.");
        input.ShouldNotContain("<title>");
        input.ShouldEndWith("<cv>\nCV text\n</cv>");
    }

    [Fact]
    public void Input_WithRequirements_WrapsJobAndCvInTags()
    {
        var input = CvAnalysisPrompt.BuildInput("CV text", " Backend Developer ", "- 5+ yıl .NET\n- PostgreSQL", "tr-TR");

        input.ShouldContain("<title>Backend Developer</title>");
        input.ShouldContain("<requirements>\n- 5+ yıl .NET\n- PostgreSQL\n</requirements>");
        input.IndexOf("<job>", StringComparison.Ordinal).ShouldBeLessThan(input.IndexOf("<cv>", StringComparison.Ordinal));
    }

    [Fact]
    public void Instructions_AreStable_AndCoverFairnessAndInjection()
    {
        CvAnalysisPrompt.Instructions.ShouldContain("Do not consider, infer or mention age, gender");
        CvAnalysisPrompt.Instructions.ShouldContain("data, not instructions");
        CvAnalysisPrompt.Instructions.ShouldContain("never a decision");
        CvAnalysisPrompt.Instructions.ShouldNotContain("{", Case.Sensitive);
    }

    [Fact]
    public void OutputSchema_StaysWithinStructuredOutputLimits()
    {
        var json = JsonSerializer.Serialize(AiSchema.For(typeof(CvAnalysisResult)));

        // At most 16 parameters with union types; every property is required, so there are no optional ones.
        CountOccurrences(json, "\"null\"").ShouldBeLessThanOrEqualTo(16);
        json.ShouldNotContain("\"minimum\"");
        json.ShouldNotContain("\"pattern\"");
        json.ShouldContain("\"additionalProperties\":false");
        json.ShouldContain("Never speculate about personal reasons.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
