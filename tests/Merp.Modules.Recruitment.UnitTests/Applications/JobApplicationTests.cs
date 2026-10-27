using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Applications;

/// <summary>Applications, their answers and candidates (PLAN R3b, D49).</summary>
public sealed class JobApplicationTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly DateTimeOffset At = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Military = Guid.CreateVersion7();
    private static readonly Guid Skills = Guid.CreateVersion7();
    private static readonly Guid Years = Guid.CreateVersion7();

    private static readonly ApplicationForm Form = new(
        RequirePhone: true,
        RequireCv: true,
        AskCoverLetter: false,
        [
            new OpeningQuestion(Military, "Askerlik?", QuestionType.YesNo, true, [], [OpeningQuestion.Yes]),
            new OpeningQuestion(Skills, "Yetkinlikler", QuestionType.MultiChoice, false, ["Excel", "SAP", "Power BI"], ["Excel", "SAP"]),
            new OpeningQuestion(Years, "Deneyim (yıl)", QuestionType.Number, false, [], null),
        ]);

    private static JobOpening Opening(bool publish = true)
    {
        var template = StageTemplate.Create(
            LocalizedString.Of("tr-TR", "Standart"),
            [
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Başvuru"), StageType.Applied, 3),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "İşe alındı"), StageType.Hired, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Reddedildi"), StageType.Rejected, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Geri çekildi"), StageType.Withdrawn, null),
            ],
            isDefault: true).Value;
        var opening = JobOpening.Draft(
            new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Guid.CreateVersion7(), [], []), Form),
            template);
        if (publish)
        {
            opening.Publish(Today, At);
        }

        return opening;
    }

    private static ApplicationTerms Terms(IReadOnlyList<ApplicationAnswer> answers, Guid? cv = null) =>
        new(new CandidateId(Guid.CreateVersion7()), "Ön yazı", answers, cv ?? Guid.CreateVersion7(), new ApplicationConsent(0, "tr-TR", null), IsInternal: false, "CAREER_PAGE");

    private static readonly CandidateContact Contact = new("Ayşe", "Yılmaz", "05321234567", "tr-TR");

    [Fact]
    public void AnApplication_StartsAtTheAppliedStage_AndRegistersItsCandidate()
    {
        var opening = Opening();

        var application = JobApplication.Submit(opening, Terms([new(Military, ["yes"]), new(Years, ["4.5"])]), "ayse@ornek.test", Contact, null, Today, At).Value;

        application.ShouldSatisfyAllConditions(
            a => a.StageId.ShouldBe(opening.Stages[0].Id),
            a => a.Status.ShouldBe(ApplicationStatus.Active),
            a => a.KnockedOut.ShouldBeFalse(),
            a => a.CoverLetter.ShouldBeNull(),
            a => a.CompanyId.ShouldBe(opening.CompanyId));
        var submitted = application.DomainEvents.OfType<ApplicationSubmitted>().ShouldHaveSingleItem();
        submitted.Email.ShouldBe("ayse@ornek.test");
    }

    [Fact]
    public void AKnockoutAnswerOutsideTheAcceptedOnes_MarksTheApplication()
    {
        JobApplication.Submit(Opening(), Terms([new(Military, ["no"])]), "a@ornek.test", Contact, null, Today, At).Value.KnockedOut.ShouldBeTrue();
        JobApplication.Submit(Opening(), Terms([new(Military, ["yes"]), new(Skills, ["Excel", "Power BI"])]), "a@ornek.test", Contact, null, Today, At).Value.KnockedOut.ShouldBeTrue();
        JobApplication.Submit(Opening(), Terms([new(Military, ["yes"]), new(Skills, ["SAP"])]), "a@ornek.test", Contact, null, Today, At).Value.KnockedOut.ShouldBeFalse();
    }

    [Fact]
    public void TheAnswers_AndTheCv_AreChecked()
    {
        var result = JobApplication.Submit(
            Opening(),
            Terms([new(Skills, ["Word"]), new(Years, ["dört"]), new(Guid.CreateVersion7(), ["?"])]) with { CvFileId = null },
            "a@ornek.test",
            Contact,
            null,
            Today,
            At);

        result.Error.Fields.Count.ShouldBe(5);
        result.Error.Fields.Select(f => f.Field).ShouldContain("cv");
        result.Error.Fields.ShouldContain(f => f.Field == $"answers.{Skills}" && f.Code == JobApplicationErrors.InvalidAnswerCode);
        result.Error.Fields.ShouldContain(f => f.Field == $"answers.{Years}" && f.Code == JobApplicationErrors.InvalidAnswerCode);
        result.Error.Fields.ShouldContain(f => f.Code == JobApplicationErrors.UnknownQuestionCode);
        result.Error.Fields.ShouldContain(f => f.Field == $"answers.{Military}" && f.Code == JobApplicationErrors.AnswerRequiredCode);
    }

    [Fact]
    public void AnOpeningThatDoesNotTakeApplications_RefusesThem() =>
        JobApplication.Submit(Opening(publish: false), Terms([new(Military, ["yes"])]), "a@ornek.test", Contact, null, Today, At)
            .Error.ShouldBe(JobApplicationErrors.OpeningClosed);

    [Fact]
    public void ACandidate_IsOnePerEmail_AndEachApplicationRefreshesTheContact()
    {
        var candidate = Candidate.Register(new CandidateId(Guid.CreateVersion7()), "  Ayse@Ornek.TEST ", Contact, "CAREER_PAGE", null, At);
        candidate.Email.ShouldBe("ayse@ornek.test");

        candidate.Applied(new CandidateContact("Ayşe", "Kaya", null, null), Guid.CreateVersion7(), At.AddDays(1));
        candidate.ShouldSatisfyAllConditions(
            c => c.LastName.ShouldBe("Kaya"),
            c => c.Phone.ShouldBe("05321234567"),
            c => c.Culture.ShouldBe("tr-TR"),
            c => c.EmployeeId.ShouldNotBeNull(),
            c => c.LastAppliedAt.ShouldBe(At.AddDays(1)));

        candidate.ConsentToPool(2, At);
        candidate.PoolConsentRevision.ShouldBe(2);
    }
}
