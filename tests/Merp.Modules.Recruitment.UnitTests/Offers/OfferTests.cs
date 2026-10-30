using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Offers;

/// <summary>Job offers (PLAN R6, D53).</summary>
public sealed class OfferTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTimeOffset At = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();
    private static readonly Guid Manager = Guid.CreateVersion7();
    private static readonly string TokenHash = new('a', JobOffer.TokenHashLength);

    private static readonly OfferTerms Terms = new("Satış Uzmanı", "M2", Today.AddDays(30), 90_000m, "try", SalaryPeriod.Monthly, "Özel sağlık sigortası", Today.AddDays(7));

    private static (JobOpening Opening, JobApplication Application) Pipeline()
    {
        var template = StageTemplate.Create(
            LocalizedString.Of("tr-TR", "Standart"),
            [
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Başvuru"), StageType.Applied, 3),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Teklif"), StageType.Offer, 5),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "İşe alındı"), StageType.Hired, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Reddedildi"), StageType.Rejected, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Geri çekildi"), StageType.Withdrawn, null),
            ],
            isDefault: true).Value;
        var opening = JobOpening.Draft(
            new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Manager, [], []), ApplicationForm.Default),
            template);
        opening.Publish(Today, At);
        var candidate = Candidate.Add("ayse@ornek.test", new CandidateContact("Ayşe", "Yılmaz", null, null), CandidateProfile.Empty, "REFERRAL", null);
        return (opening, JobApplication.AddFromPool(opening, candidate, Recruiter, At).Value);
    }

    private static JobOffer Drafted(JobApplication application, JobOpening opening) =>
        JobOffer.Draft("TKL-2026-00001", application, opening, null, Recruiter, Terms).Value;

    [Fact]
    public void AnOffer_IsApproved_Sent_AndAccepted_ThenTheApplicationIsHired()
    {
        var (opening, application) = Pipeline();
        var offer = Drafted(application, opening);
        offer.ShouldSatisfyAllConditions(
            o => o.Status.ShouldBe(OfferStatus.Draft),
            o => o.Currency.ShouldBe("TRY"),
            o => o.HiringManagerUserId.ShouldBe(Manager),
            o => o.CompanyId.ShouldBe(application.CompanyId),
            o => o.IsOpen.ShouldBeTrue());

        offer.Submit(true, Today, At).IsSuccess.ShouldBeTrue();
        offer.ShouldSatisfyAllConditions(o => o.Status.ShouldBe(OfferStatus.Pending), o => o.IsAboveBudget.ShouldBeTrue());
        offer.Update(Terms).Error.ShouldBe(JobOfferErrors.NotEditable);
        offer.Send(Guid.CreateVersion7(), TokenHash, Today, At).Error.ShouldBe(JobOfferErrors.NotApproved);

        offer.Complete(true, At.AddHours(2));
        offer.Status.ShouldBe(OfferStatus.Approved);
        var letter = Guid.CreateVersion7();
        offer.Send(letter, TokenHash, Today, At.AddHours(3)).IsSuccess.ShouldBeTrue();
        offer.ShouldSatisfyAllConditions(o => o.Status.ShouldBe(OfferStatus.Sent), o => o.LetterFileId.ShouldBe(letter), o => o.ResponseTokenHash.ShouldBe(TokenHash));

        offer.Respond(true, "Başlangıç tarihi uygun", OfferResponseChannel.Link, Recruiter, Today.AddDays(2), At.AddDays(2)).IsSuccess.ShouldBeTrue();
        offer.ShouldSatisfyAllConditions(
            o => o.Status.ShouldBe(OfferStatus.Accepted),
            o => o.RespondedVia.ShouldBe(OfferResponseChannel.Link),
            o => o.RespondedByUserId.ShouldBeNull(),
            o => o.ResponseTokenHash.ShouldBe(TokenHash),
            o => o.IsOpen.ShouldBeFalse());
        offer.Respond(false, null, OfferResponseChannel.Hr, Recruiter, Today, At).Error.ShouldBe(JobOfferErrors.NotSent);

        // PLAN R6 acceptance: the accepted offer opens the hire step
        application.MoveTo(opening, opening.Stages.Single(s => s.Type == StageType.Hired).Id, null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.OfferRequired);
        application.Hire(opening, offer, null, Recruiter, At.AddDays(3)).IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Hired);
    }

    [Fact]
    public void OnlyAnAcceptedOfferOfTheApplication_Hires()
    {
        var (opening, application) = Pipeline();
        var (_, other) = Pipeline();
        var offer = Drafted(application, opening);

        application.Hire(opening, offer, null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.OfferRequired);
        other.Hire(opening, offer, null, Recruiter, At).Error.ShouldBe(JobApplicationErrors.OfferRequired);
    }

    [Fact]
    public void AReturnedOffer_IsCorrectedAndSentAgain_AndARejectedOneStops()
    {
        var (opening, application) = Pipeline();
        var offer = Drafted(application, opening);
        offer.Submit(false, Today, At);

        offer.Return("Ücreti bütçeye çekin");
        offer.ShouldSatisfyAllConditions(o => o.Status.ShouldBe(OfferStatus.Returned), o => o.ReturnComment.ShouldBe("Ücreti bütçeye çekin"));
        offer.Update(Terms with { Salary = 80_000m }).IsSuccess.ShouldBeTrue();
        offer.Submit(false, Today, At).IsSuccess.ShouldBeTrue();
        offer.ReturnComment.ShouldBeNull();

        offer.Complete(false, At);
        offer.ShouldSatisfyAllConditions(o => o.Status.ShouldBe(OfferStatus.Rejected), o => o.IsOpen.ShouldBeFalse());
        offer.Withdraw(null).Error.ShouldBe(JobOfferErrors.NotOpen);
    }

    [Fact]
    public void AnOffer_EndsBeforeItsStart_AndIsAnsweredUntilItsLastDay()
    {
        var (opening, application) = Pipeline();
        JobOffer.Draft("TKL-2026-00002", application, opening, null, Recruiter, Terms with { ExpiresOn = Terms.StartDate.AddDays(1) })
            .Error.ShouldBe(JobOfferErrors.ExpiresAfterStart);

        var offer = Drafted(application, opening);
        offer.Submit(false, Terms.ExpiresOn.AddDays(1), At).Error.ShouldBe(JobOfferErrors.ExpiresInThePast);
        offer.Submit(false, Today, At);
        offer.Complete(true, At);
        offer.Send(Guid.CreateVersion7(), TokenHash, Today, At);

        // The last day still counts; the day after, the answer comes too late and the job closes the offer
        offer.Expire(Terms.ExpiresOn).ShouldBeFalse();
        offer.Respond(true, null, OfferResponseChannel.Hr, Recruiter, Terms.ExpiresOn.AddDays(1), At).Error.ShouldBe(JobOfferErrors.Expired);
        offer.Expire(Terms.ExpiresOn.AddDays(1)).ShouldBeTrue();
        offer.Status.ShouldBe(OfferStatus.Expired);
    }

    [Fact]
    public void AWithdrawal_ClosesAnOpenApproval()
    {
        var (opening, application) = Pipeline();
        var draft = Drafted(application, opening);
        draft.Withdraw(null).Value.ShouldBeFalse();

        var pending = Drafted(application, opening);
        pending.Submit(false, Today, At);
        pending.Withdraw("Aday başka teklif kabul etti").Value.ShouldBeTrue();
        pending.ShouldSatisfyAllConditions(o => o.Status.ShouldBe(OfferStatus.Withdrawn), o => o.WithdrawalReason.ShouldBe("Aday başka teklif kabul etti"));

        application.Reject(opening, "SKILLS", null, null, Recruiter, At);
        JobOffer.Draft("TKL-2026-00003", application, opening, null, Recruiter, Terms).Error.ShouldBe(JobOfferErrors.ApplicationNotActive);
    }

    [Theory]
    [InlineData(90_000, "TRY", SalaryPeriod.Monthly, null, null, false)]
    [InlineData(90_000, "TRY", SalaryPeriod.Monthly, 90_000, "TRY", false)]
    [InlineData(90_001, "TRY", SalaryPeriod.Monthly, 90_000, "TRY", true)]
    [InlineData(1_080_000, "TRY", SalaryPeriod.Yearly, 90_000, "TRY", false)]
    [InlineData(1_200_000, "TRY", SalaryPeriod.Yearly, 90_000, "TRY", true)]
    [InlineData(3_000, "EUR", SalaryPeriod.Monthly, 90_000, "TRY", true)]
    public void TheBudget_IsTheRequisitionsMonthlyRange(int salary, string currency, SalaryPeriod period, int? max, string? rangeCurrency, bool above)
    {
        var (opening, application) = Pipeline();
        var offer = JobOffer.Draft("TKL-2026-00004", application, opening, null, Recruiter, Terms with { Salary = (decimal)salary, Currency = currency, Period = period }).Value;

        offer.ExceedsBudget(max is null ? null : (decimal)max, rangeCurrency).ShouldBe(above);
    }
}
