using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Candidates;

/// <summary>The candidate pool (PLAN R4b, D50): profile, tags, notes, CV text, merging and adding to an opening.</summary>
public sealed class CandidateTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    private static Candidate Added(string email = "ayse@ornek.test", string? phone = "0532 123 45 67") =>
        Candidate.Add(email, new CandidateContact("Ayşe", "Yılmaz", phone, null), CandidateProfile.Empty with { CurrentTitle = "Satış Uzmanı" }, "REFERRAL", Guid.CreateVersion7());

    [Fact]
    public void PhoneNumbers_AreComparedByTheirLastTenDigits()
    {
        Candidate.NormalizePhone("+90 (532) 123 45 67").ShouldBe("5321234567");
        Candidate.NormalizePhone("05321234567").ShouldBe("5321234567");
        Candidate.NormalizePhone("532-123-4567").ShouldBe("5321234567");
        Candidate.NormalizePhone(" - ").ShouldBeNull();
        Added().NormalizedPhone.ShouldBe("5321234567");
    }

    [Fact]
    public void ACandidateHrAdds_HasAProfile_AndNoApplicationYet()
    {
        var candidate = Added(" Ayse@Ornek.TEST ");

        candidate.ShouldSatisfyAllConditions(
            c => c.Email.ShouldBe("ayse@ornek.test"),
            c => c.CurrentTitle.ShouldBe("Satış Uzmanı"),
            c => c.Source.ShouldBe("REFERRAL"),
            c => c.ReferrerEmployeeId.ShouldNotBeNull(),
            c => c.LastAppliedAt.ShouldBeNull());
        Should.Throw<ArgumentOutOfRangeException>(() =>
            candidate.Update(candidate.Email, new CandidateContact("Ayşe", "Yılmaz", null, null), CandidateProfile.Empty with { ExperienceYears = 61 }, "REFERRAL", null));
    }

    [Fact]
    public void Tags_AreDistinct_AndNotes_FeedTheSearch()
    {
        var candidate = Added();
        candidate.SetTags(["SALES", "SALES", "ENGLISH"], "Satış İngilizce");
        candidate.Tags.ShouldBe(["SALES", "ENGLISH"]);
        candidate.TagText.ShouldBe("Satış İngilizce");

        var first = candidate.AddNote("Excel bilgisi çok iyi", [Recruiter, Recruiter], Recruiter, At);
        candidate.AddNote("Maaş beklentisi yüksek", [], Recruiter, At.AddHours(1));
        first.MentionedUserIds.ShouldBe([Recruiter]);
        candidate.NotesText.ShouldBe("Excel bilgisi çok iyi\nMaaş beklentisi yüksek");

        candidate.RemoveNote(first.Id, Guid.CreateVersion7()).Error.ShouldBe(CandidateErrors.NotNoteAuthor);
        candidate.RemoveNote(first.Id, Recruiter).IsSuccess.ShouldBeTrue();
        candidate.NotesText.ShouldBe("Maaş beklentisi yüksek");
        candidate.RemoveNote(first.Id, Recruiter).Error.ShouldBe(CommonErrors.NotFound);
    }

    [Fact]
    public void TheCvText_IsKeptForTheLatestCvOnly()
    {
        var candidate = Added();
        var older = Guid.CreateVersion7();
        var latest = Guid.CreateVersion7();
        candidate.ReplaceCv(older);
        candidate.ReplaceCv(latest);

        candidate.IndexCv(older, "eski özgeçmiş");
        candidate.CvTextFileId.ShouldBeNull();

        candidate.IndexCv(latest, "SAP ve Excel deneyimi");
        candidate.ShouldSatisfyAllConditions(c => c.CvTextFileId.ShouldBe(latest), c => c.CvText.ShouldBe("SAP ve Excel deneyimi"));

        // A scanned CV without text is marked read
        var scanned = Guid.CreateVersion7();
        candidate.ReplaceCv(scanned);
        candidate.IndexCv(scanned, null);
        candidate.ShouldSatisfyAllConditions(c => c.CvTextFileId.ShouldBe(scanned), c => c.CvText.ShouldBeNull());
    }

    [Fact]
    public void AMerge_FillsTheGaps_JoinsTagsAndNotes_AndRemovesTheDuplicateLater()
    {
        var target = Candidate.Add("ayse@ornek.test", new CandidateContact("Ayşe", "Yılmaz", null, null), CandidateProfile.Empty, "CAREER_PAGE", null);
        target.SetTags(["SALES"], "Satış");
        var duplicate = Added("ayse.yilmaz@ornek.test");
        duplicate.SetTags(["ENGLISH"], "İngilizce");
        duplicate.ReplaceCv(Guid.CreateVersion7());
        duplicate.ConsentToPool(3, At);
        duplicate.AddNote("Referansla geldi", [], Recruiter, At);

        target.Absorb(duplicate, "Satış İngilizce").IsSuccess.ShouldBeTrue();

        target.ShouldSatisfyAllConditions(
            c => c.Email.ShouldBe("ayse@ornek.test"),
            c => c.Phone.ShouldBe("0532 123 45 67"),
            c => c.CurrentTitle.ShouldBe("Satış Uzmanı"),
            c => c.Tags.ShouldBe(["SALES", "ENGLISH"]),
            c => c.CvFileId.ShouldBe(duplicate.CvFileId),
            c => c.PoolConsentRevision.ShouldBe(3),
            c => c.Notes.ShouldHaveSingleItem().Body.ShouldBe("Referansla geldi"),
            c => c.NotesText.ShouldBe("Referansla geldi"));
        target.DomainEvents.OfType<CandidateMerged>().ShouldHaveSingleItem().DuplicateId.ShouldBe(duplicate.Id);
        target.Absorb(target, null).Error.ShouldBe(CandidateErrors.MergeSelf);
    }

    [Fact]
    public void ACandidateOfThePool_IsAddedToAnOpenOpening_WithoutAForm()
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
            new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Internal, null, null, new OpeningTeam(Guid.CreateVersion7(), [], []), ApplicationForm.Default),
            template);
        var candidate = Added();
        candidate.ReplaceCv(Guid.CreateVersion7());

        JobApplication.AddFromPool(opening, candidate, Recruiter, At).Error.ShouldBe(JobApplicationErrors.OpeningClosed);
        opening.Publish(DateOnly.FromDateTime(At.UtcDateTime), At);
        opening.Hold();

        var application = JobApplication.AddFromPool(opening, candidate, Recruiter, At).Value;
        application.ShouldSatisfyAllConditions(
            a => a.CandidateId.ShouldBe(candidate.Id),
            a => a.StageId.ShouldBe(opening.Stages[0].Id),
            a => a.Consent.ShouldBeNull(),
            a => a.CvFileId.ShouldBe(candidate.CvFileId),
            a => a.Source.ShouldBe("REFERRAL"),
            a => a.Answers.ShouldBeEmpty());
        application.History.ShouldHaveSingleItem().UserId.ShouldBe(Recruiter);
        application.DomainEvents.ShouldBeEmpty();

        var other = CandidateId.From(Guid.CreateVersion7());
        application.MoveToCandidate(other);
        application.CandidateId.ShouldBe(other);
    }
}
