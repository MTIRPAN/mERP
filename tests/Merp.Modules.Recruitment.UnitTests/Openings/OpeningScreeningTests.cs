using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Openings;

/// <summary>The batch screening of an opening (PLAN A6, D59): waiting, sent, held, going on the next night, done.</summary>
public sealed class OpeningScreeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();
    private static readonly string Hash = new('a', 64);

    private static JobOpening Opening(string? qualifications = "<ul><li>5 yıl B2B satış</li></ul>", string title = "Satış Uzmanı")
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
        return JobOpening.Draft(
            new OpeningTerms(Guid.CreateVersion7(), null, title, null, qualifications, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Recruiter, [], []), ApplicationForm.Default),
            template);
    }

    private static ScreeningItem Item() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), new Dictionary<string, int> { ["name"] = 2 }, false);

    [Fact]
    public void AScreening_GoesOnTheNextNight_UntilEveryApplicationIsAnalyzed()
    {
        var opening = Opening();
        var screening = OpeningScreening.Request(opening, Recruiter, Now);
        screening.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Queued), s => s.CompanyId.ShouldBe(opening.CompanyId), s => s.IsOpen.ShouldBeTrue());

        screening.Submit("msgbatch_1", [Item(), Item()], Hash, 0.04m, Now).IsSuccess.ShouldBeTrue();
        screening.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Submitted), s => s.Items.Count.ShouldBe(2), s => s.BatchId.ShouldBe("msgbatch_1"));
        screening.Submit("msgbatch_2", [Item()], Hash, 0.02m, Now).Error.ShouldBe(OpeningScreeningErrors.NotWaiting);
        screening.Cancel(Recruiter, Now).Error.ShouldBe(OpeningScreeningErrors.NotCancellable);

        // The night's limit left some out: it waits for the next night
        screening.Collect(1, 1, moreToDo: true, Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        screening.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Queued), s => s.Items.ShouldBeEmpty(), s => s.BatchId.ShouldBeNull(), s => s.CompletedAt.ShouldBeNull());

        screening.Submit("msgbatch_2", [Item()], Hash, 0.02m, Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        screening.Collect(1, 0, moreToDo: false, Now.AddDays(1).AddHours(1)).IsSuccess.ShouldBeTrue();
        screening.ShouldSatisfyAllConditions(
            s => s.Status.ShouldBe(ScreeningStatus.Completed),
            s => s.SucceededCount.ShouldBe(2),
            s => s.FailedCount.ShouldBe(1),
            s => s.CompletedAt.ShouldBe(Now.AddDays(1).AddHours(1)),
            s => s.IsOpen.ShouldBeFalse());
        screening.Collect(0, 0, false, Now).Error.ShouldBe(OpeningScreeningErrors.NotSubmitted);
    }

    [Fact]
    public void AHeldScreening_IsTriedAgain_AndOneNotSentYetMayBeCancelled()
    {
        var screening = OpeningScreening.Request(Opening(), Recruiter, Now);
        screening.Hold("common.errors.ai_budget_exceeded").IsSuccess.ShouldBeTrue();
        screening.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Held), s => s.HoldReason.ShouldBe("common.errors.ai_budget_exceeded"), s => s.IsOpen.ShouldBeTrue());

        screening.Submit("msgbatch_1", [Item()], Hash, 0.02m, Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        screening.HoldReason.ShouldBeNull();

        var waiting = OpeningScreening.Request(Opening(), Recruiter, Now);
        waiting.Cancel(Recruiter, Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        waiting.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Cancelled), s => s.CancelledByUserId.ShouldBe(Recruiter), s => s.IsOpen.ShouldBeFalse());
        waiting.Hold("x").Error.ShouldBe(OpeningScreeningErrors.NotWaiting);

        var empty = OpeningScreening.Request(Opening(), Recruiter, Now);
        empty.Finish(Now).IsSuccess.ShouldBeTrue();
        empty.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Completed), s => s.SucceededCount.ShouldBe(0));
    }

    [Fact]
    public void TheRequirementsDigest_ChangesWithTheTitleOrTheRequirements_NotWithTheMarkup()
    {
        var hash = ApplicationCvAnalyzer.RequirementsHash(Opening());

        hash.Length.ShouldBe(64);
        ApplicationCvAnalyzer.RequirementsHash(Opening()).ShouldBe(hash);
        ApplicationCvAnalyzer.RequirementsHash(Opening("<ul>\n<li>5 yıl B2B satış</li>\n</ul>")).ShouldBe(ApplicationCvAnalyzer.RequirementsHash(Opening("<ul><li>5 yıl B2B satış</li></ul>")));
        ApplicationCvAnalyzer.RequirementsHash(Opening("<ul><li>3 yıl B2B satış</li></ul>")).ShouldNotBe(hash);
        ApplicationCvAnalyzer.RequirementsHash(Opening(title: "Kıdemli Satış Uzmanı")).ShouldNotBe(hash);
    }
}
