using Merp.Modules.Platform.Application.Documents;

namespace Merp.Modules.Platform.UnitTests.Documents;

/// <summary>What a template draft sends (PLAN A10, D63): the type's variables and texts, the sample, and what to change.</summary>
public sealed class DocumentTemplateDraftPromptTests
{
    [Fact]
    public void TheInput_GivesTheVariablesTextsAndSample_AndTheCurrentTemplateOnlyWhenRevising()
    {
        var sample = new Dictionary<string, object?> { ["letter"] = new Dictionary<string, object?> { ["subject"] = "Örnek konu" } };
        var texts = new Dictionary<string, string> { ["subject"] = "Konu", ["date"] = "Tarih" };

        var fresh = DocumentTemplateDraftPrompt.BuildInput("Antetli genel yazı", "tr-TR", [("letter.subject", "Yazının konusu")], texts, sample, "Ortada konu", null, null, null);
        var revised = DocumentTemplateDraftPrompt.BuildInput("Antetli genel yazı", "tr-TR", [], texts, sample, "Konuyu kalın yaz", "<p>{{ letter.subject }}</p>", "p {}", "parse line=1");

        fresh.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<document_type>Antetli genel yazı</document_type>"),
            i => i.ShouldContain("<variable path=\"letter.subject\">Yazının konusu</variable>"),
            i => i.ShouldContain("<text name=\"date\">Tarih</text>\n<text name=\"subject\">Konu</text>"),
            i => i.ShouldContain("\"subject\":\"\\u00D6rnek konu\""),
            i => i.ShouldNotContain("<current_body>"),
            i => i.ShouldNotContain("<previous_attempt_error>"),
            i => i.ShouldEndWith("<description>\nOrtada konu\n</description>"));
        revised.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<current_body>\n<p>{{ letter.subject }}</p>\n</current_body>\n<current_css>\np {}\n</current_css>"),
            i => i.ShouldContain("<previous_attempt_error>parse line=1</previous_attempt_error>"));
    }
}
