using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Hiring;

/// <summary>From an accepted offer to a filled requisition (PLAN R7, D54).</summary>
public sealed class HiringTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly DateTimeOffset At = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();
    private static readonly Guid Manager = Guid.CreateVersion7();

    private static JobOpening Opening(int headcount = 1)
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
            new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, headcount, OpeningVisibility.Public, null, null, new OpeningTeam(Manager, [], []), ApplicationForm.Default),
            template);
        opening.Publish(Today, At);
        return opening;
    }

    private static JobApplication Apply(JobOpening opening) =>
        JobApplication.AddFromPool(opening, Candidate.Add($"{Guid.NewGuid():N}@ornek.test", new CandidateContact("Ayşe", "Yılmaz", null, null), CandidateProfile.Empty, "REFERRAL", null), Recruiter, At).Value;

    private static JobOffer Accepted(JobApplication application, JobOpening opening)
    {
        var offer = JobOffer.Draft("TKL-2026-00001", application, opening, null, Recruiter, new OfferTerms("Satış Uzmanı", null, Today.AddDays(30), 90_000m, "TRY", SalaryPeriod.Monthly, null, Today.AddDays(7))).Value;
        offer.Submit(false, Today, At);
        offer.Complete(true, At);
        offer.Send(Guid.CreateVersion7(), new string('a', JobOffer.TokenHashLength), Today, At);
        offer.Respond(true, null, OfferResponseChannel.Hr, Recruiter, Today, At);
        return offer;
    }

    [Fact]
    public void AHireRequest_KeepsTheApplicationInThePipeline_UntilHrCompletesIt()
    {
        var opening = Opening();
        var application = Apply(opening);
        var offer = Accepted(application, opening);

        application.RequestHire(offer, At).IsSuccess.ShouldBeTrue();
        application.ShouldSatisfyAllConditions(a => a.Status.ShouldBe(ApplicationStatus.Active), a => a.HireRequestedAt.ShouldBe(At));
        application.RequestHire(offer, At).Error.ShouldBe(JobApplicationErrors.HireRequested);

        // HR called it off: it may be asked again
        application.HireRequestCancelled();
        application.HireRequestedAt.ShouldBeNull();
        application.RequestHire(offer, At.AddDays(1)).IsSuccess.ShouldBeTrue();

        application.Hire(opening, offer, null, null, At.AddDays(2)).IsSuccess.ShouldBeTrue();
        application.ShouldSatisfyAllConditions(
            a => a.Status.ShouldBe(ApplicationStatus.Hired),
            a => a.HireRequestedAt.ShouldBeNull(),
            a => a.DomainEvents.OfType<ApplicationHired>().ShouldHaveSingleItem().OpeningId.ShouldBe(opening.Id));
    }

    [Fact]
    public void AHireRequest_NeedsTheAcceptedOfferOfTheApplication()
    {
        var opening = Opening();
        var application = Apply(opening);
        var other = Apply(opening);
        var draft = JobOffer.Draft("TKL-2026-00002", application, opening, null, Recruiter, new OfferTerms("Satış Uzmanı", null, Today.AddDays(30), 90_000m, "TRY", SalaryPeriod.Monthly, null, Today.AddDays(7))).Value;

        application.RequestHire(draft, At).Error.ShouldBe(JobApplicationErrors.OfferRequired);
        other.RequestHire(Accepted(application, opening), At).Error.ShouldBe(JobApplicationErrors.OfferRequired);
    }

    [Fact]
    public void AHire_FillsTheRequisitionsSeats_AndTheOpeningWithItsHeadcount()
    {
        // PLAN R7 acceptance: once HR completes the hires the seats are filled and the requisition is Filled
        var requisition = JobRequisition.Draft(
            "PT-2026-00001",
            Recruiter,
            new RequisitionTerms(Guid.CreateVersion7(), Guid.CreateVersion7(), null, "Satış Uzmanı", "M2", 2, RequisitionReason.NewHeadcount, null, RequisitionEmploymentType.Indefinite, null, null, null, null, "Yeni bayi kanalı", Manager));
        requisition.Fill();
        requisition.FilledCount.ShouldBe(0);
        requisition.Submit(At);
        requisition.Complete(true, At);

        requisition.Fill();
        requisition.ShouldSatisfyAllConditions(r => r.FilledCount.ShouldBe(1), r => r.Status.ShouldBe(RequisitionStatus.Approved));
        requisition.Fill();
        requisition.ShouldSatisfyAllConditions(r => r.FilledCount.ShouldBe(2), r => r.Status.ShouldBe(RequisitionStatus.Filled));

        var opening = Opening(headcount: 2);
        opening.MarkFilled(1, At).ShouldBeFalse();
        opening.MarkFilled(2, At).ShouldBeTrue();
        opening.ShouldSatisfyAllConditions(o => o.Status.ShouldBe(OpeningStatus.Filled), o => o.IsOpenOn(Today).ShouldBeFalse());
        opening.Publish(Today, At).Error.ShouldBe(JobOpeningErrors.NotPublishable);
        opening.Close("Kadrolar doldu", At).IsSuccess.ShouldBeTrue();
    }
}
