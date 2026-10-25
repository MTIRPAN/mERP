using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Openings;

/// <summary>The life of a job opening and the questions of its form (PLAN R3a, D49).</summary>
public sealed class JobOpeningTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ADraft_CopiesTheStages_AndGivesNewQuestionsIds()
    {
        var template = Template();
        var opening = JobOpening.Draft(Terms() with
        {
            Form = new ApplicationForm(true, true, false, [new OpeningQuestion(Guid.Empty, "Ehliyetiniz var mı?", QuestionType.YesNo, true, [], [OpeningQuestion.Yes])]),
            Team = new OpeningTeam(Guid.CreateVersion7(), [Recruiter, Recruiter], []),
        }, template);

        opening.Status.ShouldBe(OpeningStatus.Draft);
        opening.StageTemplateId.ShouldBe(template.Id);
        opening.Stages.Select(s => s.Id).ShouldBe(template.Stages.Select(s => s.Id));
        opening.Form.Questions.ShouldHaveSingleItem().Id.ShouldNotBe(Guid.Empty);
        opening.Team.RecruiterUserIds.ShouldBe([Recruiter]);
    }

    [Fact]
    public void Publishing_SetsTheFirstDay_HoldingPauses_AndAClosedOpeningNeverChanges()
    {
        var opening = JobOpening.Draft(Terms(), Template());

        opening.Hold().Error.ShouldBe(JobOpeningErrors.NotPublished);
        opening.Publish(Today, Now).IsSuccess.ShouldBeTrue();
        opening.PublishFrom.ShouldBe(Today);
        opening.PublishedAt.ShouldBe(Now);
        opening.IsOpenOn(Today).ShouldBeTrue();
        opening.Publish(Today, Now).Error.ShouldBe(JobOpeningErrors.NotPublishable);

        opening.Hold().IsSuccess.ShouldBeTrue();
        opening.IsOpenOn(Today).ShouldBeFalse();
        opening.Publish(Today.AddDays(1), Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        opening.PublishFrom.ShouldBe(Today);
        opening.PublishedAt.ShouldBe(Now);

        opening.Close("Doldu", Now).IsSuccess.ShouldBeTrue();
        opening.Close(null, Now).Error.ShouldBe(JobOpeningErrors.Closed);
        opening.Update(Terms(), null).Error.ShouldBe(JobOpeningErrors.Closed);
        opening.Publish(Today, Now).Error.ShouldBe(JobOpeningErrors.NotPublishable);
    }

    [Fact]
    public void AnOpeningPastItsLastDay_IsNotPublished_AndIsClosedToApplicationsOutsideItsDates()
    {
        JobOpening.Draft(Terms() with { PublishUntil = Today.AddDays(-1) }, Template()).Publish(Today, Now).Error.ShouldBe(JobOpeningErrors.Expired);

        var later = JobOpening.Draft(Terms() with { PublishFrom = Today.AddDays(3), PublishUntil = Today.AddDays(10) }, Template());
        later.Publish(Today, Now).IsSuccess.ShouldBeTrue();
        later.IsOpenOn(Today).ShouldBeFalse();
        later.IsOpenOn(Today.AddDays(3)).ShouldBeTrue();
        later.IsOpenOn(Today.AddDays(11)).ShouldBeFalse();
    }

    [Fact]
    public void TheStagesChangeOnlyInADraft_AndTheCompanyNever()
    {
        var opening = JobOpening.Draft(Terms(), Template());
        var other = Template();

        opening.Update(Terms(), other).IsSuccess.ShouldBeTrue();
        opening.StageTemplateId.ShouldBe(other.Id);
        opening.Update(Terms() with { CompanyId = Guid.CreateVersion7() }, null).Error.ShouldBe(JobOpeningErrors.FixedFields);

        opening.Publish(Today, Now);
        opening.Update(Terms(), Template()).Error.ShouldBe(JobOpeningErrors.StagesFixed);
        opening.Update(Terms() with { Title = "Kıdemli Satış Uzmanı" }, other).IsSuccess.ShouldBeTrue();
        opening.Title.ShouldBe("Kıdemli Satış Uzmanı");
    }

    [Theory]
    [InlineData(QuestionType.YesNo, new string[0], new[] { "yes" }, true)]
    [InlineData(QuestionType.YesNo, new string[0], new[] { "maybe" }, false)]
    [InlineData(QuestionType.SingleChoice, new[] { "İstanbul", "Ankara" }, new[] { "Ankara" }, true)]
    [InlineData(QuestionType.SingleChoice, new[] { "İstanbul", "Ankara" }, new[] { "İzmir" }, false)]
    [InlineData(QuestionType.SingleChoice, new[] { "İstanbul" }, new string[0], false)]
    [InlineData(QuestionType.MultiChoice, new[] { "A", "A" }, new string[0], false)]
    [InlineData(QuestionType.Text, new string[0], new[] { "x" }, false)]
    [InlineData(QuestionType.Number, new string[0], new string[0], true)]
    public void AQuestion_HoldsTogether(QuestionType type, string[] options, string[] accepted, bool valid)
    {
        var question = OpeningRules.Question(new OpeningQuestionInput(null, " Soru ", type, true, options, accepted));

        (question is not null).ShouldBe(valid);
        if (question is not null)
        {
            question.Text.ShouldBe("Soru");
            question.IsKnockout.ShouldBe(accepted.Length > 0);
            question.Options.Count.ShouldBe(type is QuestionType.SingleChoice or QuestionType.MultiChoice ? options.Length : 0);
        }
    }

    private static readonly Guid Recruiter = Guid.CreateVersion7();

    private static readonly Guid Company = Guid.CreateVersion7();

    private static OpeningTerms Terms() => new(
        Company,
        null,
        "Satış Uzmanı",
        "<p>Açıklama</p>",
        null,
        null,
        WorkModel.OnSite,
        RequisitionEmploymentType.Indefinite,
        1,
        OpeningVisibility.Public,
        null,
        null,
        new OpeningTeam(Guid.CreateVersion7(), [], []),
        ApplicationForm.Default);

    private static StageTemplate Template()
    {
        Stage S(StageType type, int? sla = null) => new(Guid.Empty, LocalizedString.Of("tr-TR", type.ToString()), type, sla);
        return StageTemplate.Create(
            LocalizedString.Of("tr-TR", "Şablon"),
            [S(StageType.Applied, 3), S(StageType.Interview, 7), S(StageType.Hired), S(StageType.Rejected), S(StageType.Withdrawn)],
            isDefault: false).Value;
    }
}
