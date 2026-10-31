using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Interviews;

/// <summary>The interview questions and the summary of the forms (PLAN A5, D58): the blind rule and what reaches the provider.</summary>
public sealed class InterviewAiTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = Guid.CreateVersion7();
    private static readonly Guid Ayse = Guid.CreateVersion7();
    private static readonly Guid Mehmet = Guid.CreateVersion7();

    private static readonly EvaluationForm Form = EvaluationForm.Check(new EvaluationForm(
        [new EvaluationSection("Teknik", [Criterion("Problem çözme", 3), Criterion("İletişim", 1)])],
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

    private static Interview Planned(JobApplication application, EvaluationForm? form, params Guid[] interviewers) =>
        Interview.Schedule(application, new InterviewTerms(InterviewType.Technical, Now.AddDays(1), Now.AddDays(1).AddHours(1), null, null, interviewers, null), form, Organizer, Now).Value;

    private static InterviewFeedback Sent(Interview interview, Guid interviewer, int score, string? comment = null) =>
        InterviewFeedback.Submit(
            interview,
            interviewer,
            [.. interview.Evaluation!.Criteria.Select(c => new CriterionScore(c.Id, score, comment))],
            [],
            score >= 3 ? Recommendation.Yes : Recommendation.No,
            null,
            Now).Value;

    [Fact]
    public void TheSummary_WaitsForEveryInterviewerOfTheApplication()
    {
        // PLAN A5: the summary shows after everyone sent their form
        var application = Application();
        var technical = Planned(application, Form, Ayse, Mehmet);
        var manager = Planned(application, Form, Organizer);
        var withoutForm = Planned(application, null, Mehmet);
        var noShow = Planned(application, Form, Ayse);
        noShow.MarkNoShow().IsSuccess.ShouldBeTrue();
        Interview[] interviews = [technical, manager, withoutForm, noShow];

        var forms = new List<InterviewFeedback> { Sent(technical, Ayse, 4) };
        FeedbackCompletion.Pending(interviews, forms).ShouldBe(2);
        forms.Add(Sent(technical, Mehmet, 2));
        FeedbackCompletion.Pending(interviews, forms).ShouldBe(1);

        // A cancelled interview, one without a form and a no-show wait for nobody
        manager.Cancel(null).IsSuccess.ShouldBeTrue();
        FeedbackCompletion.Pending(interviews, forms).ShouldBe(0);
    }

    [Fact]
    public void TheQuestionInput_GivesTheRoleAndTheCriteriaByKey()
    {
        var input = InterviewQuestionPrompt.BuildInput("Yazılım Geliştirici", "Mikroservisler geliştirecek.", null, InterviewType.Technical, Form, "tr-TR");

        input.ShouldSatisfyAllConditions(
            i => i.ShouldStartWith("Output language: Turkish"),
            i => i.ShouldContain("<interview_type>Technical</interview_type>"),
            i => i.ShouldContain("<title>Yazılım Geliştirici</title>"),
            i => i.ShouldContain("<qualifications>\n(none)\n</qualifications>"),
            i => i.ShouldContain("<criterion key=\"C1\">\n<name>Problem çözme</name>\n<score value=\"1\">Yetersiz</score>"),
            i => i.ShouldContain("<criterion key=\"C2\">\n<name>İletişim</name>"),
            i => i.ShouldContain("<question>En güçlü yanı ne?</question>"));
    }

    [Fact]
    public void TheSummaryInput_NamesInterviewersByKey_AndRedactsTheirTexts()
    {
        var application = Application();
        var interview = Planned(application, Form, Ayse, Mehmet);
        var ayse = Sent(interview, Ayse, 5, "Ayşe Yılmaz problemi hızla çözdü");
        var mehmet = Sent(interview, Mehmet, 2);

        var input = FeedbackSummaryPrompt.BuildInput(
            "Yazılım Geliştirici",
            [(FeedbackSummaryPrompt.Key(0), interview, ayse), (FeedbackSummaryPrompt.Key(1), interview, mehmet)],
            text => text.Replace("Ayşe Yılmaz", "[NAME]", StringComparison.Ordinal),
            "tr-TR");

        input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<form interviewer=\"I1\" interview_type=\"Technical\" recommendation=\"Yes\" weighted_score=\"5"),
            i => i.ShouldContain("<score criterion=\"Problem çözme\" value=\"5\">[NAME] problemi hızla çözdü</score>"),
            i => i.ShouldContain("<form interviewer=\"I2\" interview_type=\"Technical\" recommendation=\"No\""),
            i => i.ShouldNotContain("Ayşe"),
            i => i.ShouldNotContain(Ayse.ToString()));
    }
}
