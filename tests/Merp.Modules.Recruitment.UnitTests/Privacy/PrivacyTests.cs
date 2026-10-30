using Merp.Modules.Recruitment.Application.Privacy;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Privacy;

/// <summary>Consents, retention and anonymization of candidates (PLAN R8, D55).</summary>
public sealed class PrivacyTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly Guid Recruiter = Guid.CreateVersion7();
    private static readonly string TokenHash = new('b', Candidate.TokenHashLength);

    private static Candidate Registered(DateTimeOffset appliedAt) =>
        Candidate.Register(new CandidateId(Guid.CreateVersion7()), "Ayse@Ornek.test", new CandidateContact("Ayşe", "Yılmaz", "0532 123 45 67", "tr-TR"), "CAREER_PAGE", Guid.CreateVersion7(), appliedAt);

    private static JobOpening Opening()
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
            new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Recruiter, [], []), ApplicationForm.Default),
            template);
        opening.Publish(Today, At);
        return opening;
    }

    [Fact]
    public void TheRetention_CountsFromTheLatestApplicationOrConsent()
    {
        var candidate = Registered(At);
        candidate.RetentionStart.ShouldBe(At);
        CandidateRetention.EndsOn(candidate, 24).ShouldBe(Today.AddMonths(24));

        candidate.ConsentRenewalRequested(TokenHash, At.AddMonths(23));
        candidate.RenewPoolConsent(3, "tr-TR", At.AddMonths(23).AddDays(5)).IsSuccess.ShouldBeTrue();
        candidate.ShouldSatisfyAllConditions(
            c => c.PoolConsentRevision.ShouldBe(3),
            c => c.ConsentTokenHash.ShouldBeNull(),
            c => c.RetentionStart.ShouldBe(At.AddMonths(23).AddDays(5)),
            c => c.ConsentChanges.ShouldHaveSingleItem().Kind.ShouldBe(ConsentChangeKind.Renewed));

        // A withdrawal lets the retention run out from the application again
        candidate.WithdrawPoolConsent(At.AddMonths(23).AddDays(6)).IsSuccess.ShouldBeTrue();
        candidate.ShouldSatisfyAllConditions(
            c => c.PoolConsentRevision.ShouldBeNull(),
            c => c.RetentionStart.ShouldBe(At),
            c => c.ConsentChanges.Select(x => x.Kind).ShouldBe([ConsentChangeKind.Renewed, ConsentChangeKind.Withdrawn]));
    }

    [Fact]
    public void AnAnonymizedCandidate_KeepsOnlyWhatTheStatisticsNeed()
    {
        // PLAN R8: the name becomes "Anonim aday #", the contact details and notes go, the statistics stay
        var candidate = Registered(At);
        candidate.Update("ayse@ornek.test", new CandidateContact("Ayşe", "Yılmaz", "0532 123 45 67", "tr-TR"), new CandidateProfile("İstanbul", "Satış Uzmanı", "Kuzey", 6, "Lisans", "https://linkedin.example/ayse", null), "REFERRAL", null);
        candidate.SetTags(["SALES"], "Satış");
        candidate.AddNote("Mülakata çağıralım", [], Recruiter, At);
        candidate.ReplaceCv(Guid.CreateVersion7());

        candidate.Anonymize(At.AddMonths(25)).IsSuccess.ShouldBeTrue();

        candidate.ShouldSatisfyAllConditions(
            c => c.FullName.ShouldStartWith("Anonim aday #"),
            c => c.Email.ShouldEndWith("@anonim.invalid"),
            c => c.Phone.ShouldBeNull(),
            c => c.NormalizedPhone.ShouldBeNull(),
            c => c.Location.ShouldBeNull(),
            c => c.LinkedInUrl.ShouldBeNull(),
            c => c.Notes.ShouldBeEmpty(),
            c => c.NotesText.ShouldBeNull(),
            c => c.CvFileId.ShouldBeNull(),
            c => c.EmployeeId.ShouldBeNull(),
            c => c.Source.ShouldBe("REFERRAL"),
            c => c.Tags.ShouldBe(["SALES"]),
            c => c.ExperienceYears.ShouldBe(6),
            c => c.IsAnonymized.ShouldBeTrue(),
            c => c.DomainEvents.OfType<CandidateAnonymized>().ShouldHaveSingleItem().CandidateId.ShouldBe(candidate.Id));
        candidate.Anonymize(At).Error.ShouldBe(CandidateErrors.Anonymized);
        candidate.RenewPoolConsent(1, "tr-TR", At).Error.ShouldBe(CandidateErrors.Anonymized);
    }

    [Fact]
    public void TheApplicationsAndOffers_LoseTheirPersonalTexts()
    {
        var opening = Opening();
        var candidate = Candidate.Add("can@ornek.test", new CandidateContact("Can", "Demir", null, null), CandidateProfile.Empty, "REFERRAL", null);
        var application = JobApplication.AddFromPool(opening, candidate, Recruiter, At).Value;
        var offer = JobOffer.Draft("TKL-2026-00001", application, opening, null, Recruiter, new OfferTerms("Satış Uzmanı", null, Today.AddDays(30), 90_000m, "TRY", SalaryPeriod.Monthly, null, Today.AddDays(7))).Value;
        offer.Submit(false, Today, At);
        offer.Complete(true, At);
        var letter = Guid.CreateVersion7();
        offer.Send(letter, TokenHash, Today, At);
        offer.Respond(false, "Başka bir yerden teklif aldım", OfferResponseChannel.Link, null, Today, At);

        application.Anonymize();
        offer.Anonymize().ShouldBe(letter);

        application.ShouldSatisfyAllConditions(a => a.CoverLetter.ShouldBeNull(), a => a.Answers.ShouldBeEmpty(), a => a.CvFileId.ShouldBeNull(), a => a.Status.ShouldBe(ApplicationStatus.Active));
        offer.ShouldSatisfyAllConditions(o => o.LetterFileId.ShouldBeNull(), o => o.ResponseNote.ShouldBeNull(), o => o.Salary.ShouldBe(90_000m), o => o.Status.ShouldBe(OfferStatus.Declined));
    }

    [Fact]
    public void TheConsents_ComeFromTheFormsAndTheRenewals_NewestFirst()
    {
        var candidate = Registered(At);
        candidate.ConsentRenewalRequested(TokenHash, At.AddMonths(23));
        candidate.RenewPoolConsent(2, "tr-TR", At.AddMonths(23));

        var consents = CandidateRetention.Consents(candidate, []);

        consents.ShouldHaveSingleItem().ShouldBe(new CandidateConsentItem(At.AddMonths(23), CandidateConsentKind.Renewed, 2, "tr-TR", null));
        Should.Throw<ArgumentException>(() => candidate.ConsentRenewalRequested("kısa", At));
    }
}
