using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Openings;

/// <summary>AI drafts of opening texts wait for a review, and the rule list warns about exclusionary phrases (PLAN A4, D57).</summary>
public sealed class OpeningTextTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reviewer = Guid.CreateVersion7();

    [Fact]
    public void AnAiDraft_IsPublishedOnlyAfterAUserConfirmsReviewingIt()
    {
        // PLAN A4 acceptance: the draft cannot be published without the user
        var opening = Opening();
        opening.ConfirmAiDraftReviewed(Reviewer, Now);
        opening.AiDraftReviewedAt.ShouldBeNull();

        opening.DraftedWithAi(Now);
        opening.AiDraftPending.ShouldBeTrue();
        opening.Publish(Today, Now).Error.ShouldBe(JobOpeningErrors.AiDraftNotReviewed);

        opening.ConfirmAiDraftReviewed(Reviewer, Now);
        opening.ShouldSatisfyAllConditions(
            o => o.AiDraftPending.ShouldBeFalse(),
            o => o.AiDraftReviewedByUserId.ShouldBe(Reviewer),
            o => o.Publish(Today, Now).IsSuccess.ShouldBeTrue());

        // A later draft needs a new review
        opening.Hold().IsSuccess.ShouldBeTrue();
        opening.DraftedWithAi(Now.AddHours(1));
        opening.Publish(Today, Now.AddHours(1)).Error.ShouldBe(JobOpeningErrors.AiDraftNotReviewed);
    }

    [Theory]
    [InlineData("25-35 yaş arası adaylar", LanguageWarningCategory.Age, "25-35 yaş")]
    [InlineData("En fazla 30 yaşında olmalı", LanguageWarningCategory.Age, "En fazla 30 yaşında")]
    [InlineData("Genç ve dinamik bir ekip arkadaşı", LanguageWarningCategory.Age, "Genç ve dinamik")]
    [InlineData("Bayan eleman aranıyor", LanguageWarningCategory.Gender, "Bayan eleman")]
    [InlineData("ERKEK ADAY", LanguageWarningCategory.Gender, "ERKEK ADAY")]
    [InlineData("Askerliğini yapmış olmak", LanguageWarningCategory.MilitaryService, "Askerliğini")]
    [InlineData("Evli olması tercih sebebidir", LanguageWarningCategory.MaritalStatus, "Evli")]
    [InlineData("T.C. vatandaşı olmak", LanguageWarningCategory.Nationality, "T.C. vatandaşı")]
    [InlineData("Düzgün görünümlü", LanguageWarningCategory.Appearance, "Düzgün görünümlü")]
    [InlineData("Sağlık sorunu olmayan", LanguageWarningCategory.Health, "Sağlık sorunu olmayan")]
    [InlineData("We are looking for a young salesman", LanguageWarningCategory.Age, "young")]
    [InlineData("Must have completed military service", LanguageWarningCategory.MilitaryService, "military service")]
    public void TheRuleList_FindsExclusionaryPhrases_AsWritten(string text, LanguageWarningCategory category, string phrase) =>
        InclusiveLanguage.Check(OpeningTextField.Description, text)
            .ShouldContain(w => w.Category == category && w.Phrase == phrase && w.Source == LanguageWarningSource.Rule);

    [Theory]
    [InlineData("En az 5 yıl B2B satış deneyimi")]
    [InlineData("B sınıfı ehliyet sahibi, İngilizce iletişim kurabilen")]
    [InlineData("Ekip yönetiminde deneyimli, analitik düşünebilen")]
    public void JobRelatedRequirements_AreNotWarnedAbout(string text) =>
        InclusiveLanguage.Check(OpeningTextField.Qualifications, text).ShouldBeEmpty();

    [Fact]
    public void TheHtmlTexts_AreCheckedAsPlainText_OncePerPhrase()
    {
        var warnings = InclusiveLanguage.Check("Satış Uzmanı (Bayan)", "<p>Genç ve dinamik</p><ul><li>Bayan &amp; erkek</li></ul>", "<p>Askerlik: yapılmış</p>");

        warnings.Select(w => (w.Field, w.Phrase)).ShouldBe(
        [
            (OpeningTextField.Title, "Bayan"),
            (OpeningTextField.Description, "Genç ve dinamik"),
            (OpeningTextField.Description, "Bayan"),
            (OpeningTextField.Description, "erkek"),
            (OpeningTextField.Qualifications, "Askerlik"),
        ]);
        InclusiveLanguage.PlainText("<p>A &amp; B</p><p>C</p>").ShouldBe("A & B C");
    }

    private static JobOpening Opening() => JobOpening.Draft(
        new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", "<p>Açıklama</p>", null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Guid.CreateVersion7(), [], []), ApplicationForm.Default),
        StageTemplate.Create(
            LocalizedString.Of("tr-TR", "Şablon"),
            [
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Başvuru"), StageType.Applied, 3),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "İşe alındı"), StageType.Hired, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Reddedildi"), StageType.Rejected, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Geri çekildi"), StageType.Withdrawn, null),
            ],
            isDefault: false).Value);
}
