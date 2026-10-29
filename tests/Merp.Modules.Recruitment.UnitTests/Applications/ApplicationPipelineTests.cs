using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Applications;

/// <summary>Applications through the pipeline and the pipeline of an opening (PLAN R4a, D50).</summary>
public sealed class ApplicationPipelineTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static readonly DateTimeOffset At = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    private static JobOpening Opening()
    {
        var template = StageTemplate.Create(
            LocalizedString.Of("tr-TR", "Standart"),
            [
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Başvuru"), StageType.Applied, 3),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Ön eleme"), StageType.Screening, 5),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Mülakat"), StageType.Interview, 7),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "İşe alındı"), StageType.Hired, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Reddedildi"), StageType.Rejected, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Geri çekildi"), StageType.Withdrawn, null),
            ],
            isDefault: true).Value;
        var opening = JobOpening.Draft(
            new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Guid.CreateVersion7(), [], []), ApplicationForm.Default with { RequireCv = false }),
            template);
        opening.Publish(Today, At);
        return opening;
    }

    private static JobApplication Apply(JobOpening opening) =>
        JobApplication.Submit(
            opening,
            new ApplicationTerms(new CandidateId(Guid.CreateVersion7()), null, [], null, new ApplicationConsent(0, "tr-TR", null), IsInternal: false, "CAREER_PAGE"),
            "ayse@ornek.test",
            new CandidateContact("Ayşe", "Yılmaz", "05321234567", "tr-TR"),
            null,
            Today,
            At).Value;

    private static Guid StageOf(JobOpening opening, StageType type) => opening.Stages.First(s => s.Type == type).Id;

    /// <summary>An offer the candidate accepted (D53).</summary>
    private static JobOffer AcceptedOffer(JobApplication application, JobOpening opening)
    {
        var offer = JobOffer.Draft("TKL-2026-00001", application, opening, null, Recruiter, new OfferTerms("Satış Uzmanı", null, Today.AddDays(30), 90_000m, "TRY", SalaryPeriod.Monthly, null, Today.AddDays(7))).Value;
        offer.Submit(false, Today, At);
        offer.Complete(true, At);
        offer.Send(Guid.CreateVersion7(), new string('a', JobOffer.TokenHashLength), Today, At);
        offer.Respond(true, null, OfferResponseChannel.Hr, Recruiter, Today, At).IsSuccess.ShouldBeTrue();
        return offer;
    }

    [Fact]
    public void AMove_IsWrittenToTheHistory_WithWhoAndWhen()
    {
        var opening = Opening();
        var application = Apply(opening);
        application.History.ShouldHaveSingleItem().ShouldSatisfyAllConditions(h => h.FromStageId.ShouldBeNull(), h => h.ToStageId.ShouldBe(StageOf(opening, StageType.Applied)));

        application.MoveTo(opening, StageOf(opening, StageType.Screening), "CV uygun", Recruiter, At.AddDays(1)).IsSuccess.ShouldBeTrue();

        application.ShouldSatisfyAllConditions(
            a => a.StageId.ShouldBe(StageOf(opening, StageType.Screening)),
            a => a.StageEnteredAt.ShouldBe(At.AddDays(1)),
            a => a.Status.ShouldBe(ApplicationStatus.Active));
        application.History.Last().ShouldSatisfyAllConditions(
            h => h.Sequence.ShouldBe(2),
            h => h.FromStageId.ShouldBe(StageOf(opening, StageType.Applied)),
            h => h.ToStageId.ShouldBe(StageOf(opening, StageType.Screening)),
            h => h.Note.ShouldBe("CV uygun"),
            h => h.UserId.ShouldBe(Recruiter),
            h => h.At.ShouldBe(At.AddDays(1)));
    }

    [Fact]
    public void Rejection_AndWithdrawal_NeedTheirOwnStep_AndUnknownStagesAreRefused()
    {
        var opening = Opening();
        var application = Apply(opening);

        application.MoveTo(opening, StageOf(opening, StageType.Rejected), null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.NeedsReason);
        application.MoveTo(opening, StageOf(opening, StageType.Withdrawn), null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.NeedsReason);
        application.MoveTo(opening, Guid.CreateVersion7(), null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.UnknownStage);
        application.History.Count.ShouldBe(1);
    }

    [Fact]
    public void ARejection_KeepsTheReason_TheEmailTime_AndTheStageItLeftFrom()
    {
        var opening = Opening();
        var application = Apply(opening);
        application.MoveTo(opening, StageOf(opening, StageType.Interview), null, Recruiter, At);

        application.Reject(opening, "SKILLS", "Deneyim yetersiz", At.AddDays(2), Recruiter, At.AddHours(1)).IsSuccess.ShouldBeTrue();

        application.ShouldSatisfyAllConditions(
            a => a.Status.ShouldBe(ApplicationStatus.Rejected),
            a => a.StageId.ShouldBe(StageOf(opening, StageType.Rejected)),
            a => a.PreviousStageId.ShouldBe(StageOf(opening, StageType.Interview)),
            a => a.ReasonCode.ShouldBe("SKILLS"),
            a => a.RejectionEmailDueAt.ShouldBe(At.AddDays(2)));
        application.History.Last().ShouldSatisfyAllConditions(h => h.Status.ShouldBe(ApplicationStatus.Rejected), h => h.ReasonCode.ShouldBe("SKILLS"));

        // Out of the pipeline nothing moves until it is reopened
        application.MoveTo(opening, StageOf(opening, StageType.Screening), null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.NotActive);
        application.Withdraw(opening, "OTHER_OFFER", null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.NotActive);
        Should.Throw<ArgumentOutOfRangeException>(() => Apply(opening).Reject(opening, "SKILLS", null, At.AddDays(15), Recruiter, At));
    }

    [Fact]
    public void Reopening_ReturnsToTheStageItLeftFrom_AndDropsTheWaitingEmail()
    {
        var opening = Opening();
        var application = Apply(opening);
        application.MoveTo(opening, StageOf(opening, StageType.Screening), null, Recruiter, At);
        application.Reject(opening, "SKILLS", null, At.AddDays(3), Recruiter, At);

        application.Reopen(opening, "Yanlışlıkla reddedildi", Recruiter, At.AddDays(1)).IsSuccess.ShouldBeTrue();

        application.ShouldSatisfyAllConditions(
            a => a.Status.ShouldBe(ApplicationStatus.Active),
            a => a.StageId.ShouldBe(StageOf(opening, StageType.Screening)),
            a => a.ReasonCode.ShouldBeNull(),
            a => a.RejectionEmailDueAt.ShouldBeNull(),
            a => a.PreviousStageId.ShouldBeNull());
        application.Reopen(opening, null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.NotClosed);

        // A withdrawal is reopened the same way; hiring takes it out of the pipeline for good
        application.Withdraw(opening, "OTHER_OFFER", null, Recruiter, At).IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Withdrawn);
        application.Reopen(opening, null, Recruiter, At).IsSuccess.ShouldBeTrue();
        application.MoveTo(opening, StageOf(opening, StageType.Hired), null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.OfferRequired);
        application.Hire(opening, AcceptedOffer(application, opening), null, Recruiter, At).IsSuccess.ShouldBeTrue();
        application.ShouldSatisfyAllConditions(a => a.Status.ShouldBe(ApplicationStatus.Hired), a => a.StageId.ShouldBe(StageOf(opening, StageType.Hired)));
        application.Reopen(opening, null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.NotClosed);
        application.History.Select(h => h.Sequence).ShouldBe([1, 2, 3, 4, 5, 6, 7]);
    }

    [Fact]
    public void TheRejectionEmail_IsMarkedSent()
    {
        var opening = Opening();
        var application = Apply(opening);
        application.Reject(opening, "SKILLS", null, At, Recruiter, At);

        application.RejectionEmailSent(At.AddMinutes(15));

        application.ShouldSatisfyAllConditions(a => a.RejectionEmailDueAt.ShouldBeNull(), a => a.RejectionEmailSentAt.ShouldBe(At.AddMinutes(15)));
    }

    [Fact]
    public void AnOpeningsPipeline_Changes_ButStagesInUseStay()
    {
        var opening = Opening();
        var screening = opening.Stages.Single(s => s.Type == StageType.Screening);
        var interview = opening.Stages.Single(s => s.Type == StageType.Interview);
        var assessment = new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Vaka çalışması"), StageType.Assessment, 4);
        List<Stage> Without(Stage removed) => [.. opening.Stages.Where(s => s != removed)];

        opening.ChangeStages(Without(screening), new HashSet<Guid> { screening.Id }).Error.ShouldBe(JobOpeningErrors.StageInUse);

        var changed = Without(interview);
        changed.Insert(2, assessment);
        opening.ChangeStages(changed, new HashSet<Guid> { screening.Id }).IsSuccess.ShouldBeTrue();
        opening.Stages.Select(s => s.Type).ShouldBe([StageType.Applied, StageType.Screening, StageType.Assessment, StageType.Hired, StageType.Rejected, StageType.Withdrawn]);
        opening.Stages[2].Id.ShouldNotBe(Guid.Empty);

        // The rules of a template hold
        opening.ChangeStages([.. opening.Stages.Skip(1)], new HashSet<Guid>()).Error.ShouldBe(StageTemplateErrors.RequiredStages);
        opening.Close(null, At);
        opening.ChangeStages(opening.Stages, new HashSet<Guid>()).Error.ShouldBe(JobOpeningErrors.Closed);
    }
}
