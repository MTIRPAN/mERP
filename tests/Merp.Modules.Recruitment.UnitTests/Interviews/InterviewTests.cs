using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Interviews;

/// <summary>Interviews, evaluation forms and the interviewers' forms (PLAN R5, D52).</summary>
public sealed class InterviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = Guid.CreateVersion7();
    private static readonly Guid Ayse = Guid.CreateVersion7();
    private static readonly Guid Mehmet = Guid.CreateVersion7();

    private static readonly EvaluationForm Form = EvaluationForm.Check(new EvaluationForm(
        [
            new EvaluationSection("Teknik", [Criterion("Problem çözme", 3), Criterion("Kod kalitesi", 1)]),
            new EvaluationSection("Takım", [Criterion("İletişim", 1)]),
        ],
        [new EvaluationQuestion(Guid.Empty, "En güçlü yanı ne?")])).Value;

    private static EvaluationCriterion Criterion(string name, decimal weight) =>
        new(Guid.Empty, name, ["Yetersiz", "Zayıf", "Yeterli", "İyi", "Çok iyi"], weight);

    private static JobApplication Application()
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
            new OpeningTerms(Guid.CreateVersion7(), null, "Yazılım Geliştirici", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Organizer, [], []), ApplicationForm.Default),
            template);
        opening.Publish(DateOnly.FromDateTime(Now.UtcDateTime), Now);
        return JobApplication.AddFromPool(opening, Candidate.Add("ayse@ornek.test", new CandidateContact("Ayşe", "Yılmaz", null, null), CandidateProfile.Empty, "REFERRAL", null), Organizer, Now).Value;
    }

    private static InterviewTerms Terms(DateTimeOffset start, params Guid[] interviewers) =>
        new(InterviewType.Technical, start, start.AddHours(1), null, "https://meet.example/abc", interviewers.Length > 0 ? interviewers : [Ayse, Mehmet], "CV'deki projeleri sorun");

    private static Interview Planned(DateTimeOffset? start = null) =>
        Interview.Schedule(Application(), Terms(start ?? Now.AddDays(3)), Form, Organizer, Now).Value;

    [Fact]
    public void AnInterview_IsPlanned_WithItsTimeAndInterviewers_AndChecked()
    {
        var interview = Planned();
        interview.ShouldSatisfyAllConditions(
            i => i.Status.ShouldBe(InterviewStatus.Scheduled),
            i => i.InterviewerUserIds.ShouldBe([Ayse, Mehmet]),
            i => i.Sequence.ShouldBe(0),
            i => i.Evaluation.ShouldBe(Form),
            i => i.DayReminderSentAt.ShouldBeNull());

        var application = Application();
        Interview.Schedule(application, Terms(Now.AddDays(1)) with { EndsAt = Now.AddDays(1) }, null, Organizer, Now).Error.ShouldBe(InterviewErrors.InvalidTime);
        Interview.Schedule(application, Terms(Now.AddDays(1)) with { EndsAt = Now.AddDays(1).AddHours(13) }, null, Organizer, Now).Error.ShouldBe(InterviewErrors.InvalidTime);
        Interview.Schedule(application, Terms(Now.AddDays(1)) with { InterviewerUserIds = [] }, null, Organizer, Now).Error.ShouldBe(InterviewErrors.Interviewers);
    }

    [Fact]
    public void AChange_RaisesTheSequence_AndANewTimeGetsNewReminders()
    {
        var interview = Planned();
        interview.DayReminderSent(Now);

        interview.Reschedule(Terms(Now.AddDays(5), Ayse), Now).IsSuccess.ShouldBeTrue();
        interview.ShouldSatisfyAllConditions(
            i => i.Sequence.ShouldBe(1),
            i => i.InterviewerUserIds.ShouldBe([Ayse]),
            i => i.DayReminderSentAt.ShouldBeNull(),
            i => i.HourReminderSentAt.ShouldBeNull());

        // Within the hour no reminder is left to send
        interview.Reschedule(Terms(Now.AddMinutes(30)), Now).IsSuccess.ShouldBeTrue();
        interview.ShouldSatisfyAllConditions(i => i.DayReminderSentAt.ShouldBe(Now), i => i.HourReminderSentAt.ShouldBe(Now), i => i.Sequence.ShouldBe(2));

        interview.Cancel("Aday başka teklif aldı").IsSuccess.ShouldBeTrue();
        interview.ShouldSatisfyAllConditions(i => i.Status.ShouldBe(InterviewStatus.Cancelled), i => i.Sequence.ShouldBe(3), i => i.CancelReason.ShouldBe("Aday başka teklif aldı"));
        interview.Reschedule(Terms(Now.AddDays(1)), Now).Error.ShouldBe(InterviewErrors.NotScheduled);
        interview.Complete().Error.ShouldBe(InterviewErrors.NotScheduled);
    }

    [Fact]
    public void AForm_NeedsCriteriaWithFiveScoreDescriptionsAndAWeight()
    {
        Form.Criteria.ShouldAllBe(c => c.Id != Guid.Empty);
        Form.Questions.ShouldHaveSingleItem().Id.ShouldNotBe(Guid.Empty);

        EvaluationForm.Check(new EvaluationForm([new EvaluationSection("Boş", [])], [])).Error.ShouldBe(InterviewErrors.InvalidForm);
        EvaluationForm.Check(new EvaluationForm([new EvaluationSection("Teknik", [Criterion("X", 0)])], [])).Error.ShouldBe(InterviewErrors.InvalidForm);
        EvaluationForm.Check(new EvaluationForm([new EvaluationSection("Teknik", [Criterion("X", 1) with { ScoreDescriptions = ["a"] }])], [])).Error.ShouldBe(InterviewErrors.InvalidForm);
    }

    [Fact]
    public void AnInterviewersForm_ScoresEveryCriterion_AndIsWeighted()
    {
        var interview = Planned();
        var criteria = Form.Criteria.ToList();
        var question = Form.Questions[0];
        CriterionScore[] scores = [new(criteria[0].Id, 5, "Çok iyi"), new(criteria[1].Id, 3, null), new(criteria[2].Id, 1, null)];

        var feedback = InterviewFeedback.Submit(interview, Ayse, scores, [new(question.Id, "Analitik düşünce")], Recommendation.Yes, "Öneririm", Now).Value;

        // (5×3 + 3×1 + 1×1) / 5 = 3.8
        feedback.ShouldSatisfyAllConditions(
            f => f.WeightedScore.ShouldBe(3.8m),
            f => f.ApplicationId.ShouldBe(interview.ApplicationId),
            f => f.Answers.ShouldHaveSingleItem().Text.ShouldBe("Analitik düşünce"));

        feedback.Correct(interview, [new(criteria[0].Id, 4, null), new(criteria[1].Id, 4, null), new(criteria[2].Id, 4, null)], [], Recommendation.StrongYes, null, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        feedback.ShouldSatisfyAllConditions(f => f.WeightedScore.ShouldBe(4m), f => f.Recommendation.ShouldBe(Recommendation.StrongYes), f => f.SubmittedAt.ShouldBe(Now.AddHours(1)));

        InterviewFeedback.Submit(interview, Guid.CreateVersion7(), scores, [], Recommendation.No, null, Now).Error.ShouldBe(InterviewErrors.NotInterviewer);
        InterviewFeedback.Submit(interview, Mehmet, scores[..2], [], Recommendation.No, null, Now).Error.ShouldBe(InterviewErrors.InvalidScores);
        InterviewFeedback.Submit(interview, Mehmet, [scores[0], scores[1], scores[2] with { Score = 6 }], [], Recommendation.No, null, Now).Error.ShouldBe(InterviewErrors.InvalidScores);
        InterviewFeedback.Submit(interview, Mehmet, scores, [new(Guid.CreateVersion7(), "?")], Recommendation.No, null, Now).Error.ShouldBe(InterviewErrors.InvalidScores);

        var withoutForm = Interview.Schedule(Application(), Terms(Now.AddDays(2)), null, Organizer, Now).Value;
        InterviewFeedback.Submit(withoutForm, Ayse, scores, [], Recommendation.No, null, Now).Error.ShouldBe(InterviewErrors.NoForm);
        interview.MarkNoShow().IsSuccess.ShouldBeTrue();
        InterviewFeedback.Submit(interview, Mehmet, scores, [], Recommendation.No, null, Now).Error.ShouldBe(InterviewErrors.Cancelled);
    }

    [Fact]
    public void AnApplicationOutOfThePipeline_GetsNoInterview()
    {
        var stub = new JobOpeningStub();
        var application = JobApplication.AddFromPool(stub.Opening, Candidate.Add("can@ornek.test", new CandidateContact("Can", "Demir", null, null), CandidateProfile.Empty, "REFERRAL", null), Organizer, Now).Value;
        application.Withdraw(stub.Opening, "OTHER_OFFER", null, Organizer, Now).IsSuccess.ShouldBeTrue();

        Interview.Schedule(application, Terms(Now.AddDays(1)), null, Organizer, Now).Error.ShouldBe(InterviewErrors.ApplicationNotActive);
    }

    /// <summary>A published opening with the four stages every pipeline has.</summary>
    private sealed class JobOpeningStub
    {
        public JobOpeningStub()
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
            Opening = JobOpening.Draft(
                new OpeningTerms(Guid.CreateVersion7(), null, "Yazılım Geliştirici", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Organizer, [], []), ApplicationForm.Default),
                template);
            Opening.Publish(DateOnly.FromDateTime(Now.UtcDateTime), Now);
        }

        public JobOpening Opening { get; }
    }
}
