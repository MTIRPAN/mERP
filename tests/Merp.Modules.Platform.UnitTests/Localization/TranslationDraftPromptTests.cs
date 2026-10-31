using Merp.Modules.Platform.Application.Localization;

namespace Merp.Modules.Platform.UnitTests.Localization;

/// <summary>What a translation draft sends (PLAN A7, D60): the languages and every text under its key.</summary>
public sealed class TranslationDraftPromptTests
{
    [Fact]
    public void TheInput_NamesBothLanguages_AndListsTheTextsByKey()
    {
        var input = TranslationDraftPrompt.BuildInput(
            "tr-TR",
            "de-DE",
            [("web.common.save", "Kaydet"), ("web.localization.cultures.delete_confirm", "{name} dili silinsin mi?")]);

        input.ShouldSatisfyAllConditions(
            i => i.ShouldStartWith("Source language: Turkish"),
            i => i.ShouldContain("Output language: German"),
            i => i.ShouldContain("<text key=\"web.common.save\">Kaydet</text>"),
            i => i.ShouldContain("<text key=\"web.localization.cultures.delete_confirm\">{name} dili silinsin mi?</text>"),
            i => i.ShouldEndWith("</texts>"));
    }
}
