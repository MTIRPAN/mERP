using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Applications;

/// <summary>Personalized candidate e-mails (PLAN A5, D58): the placeholders they may use and the rejection text kept until it goes.</summary>
public sealed class CandidateEmailTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);
    private static readonly DateTimeOffset At = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();
    private static readonly CandidateEmailText Personal = CandidateEmailText.Create(" {position} başvurunuz ", "Sayın {candidate},\n\nTeknik mülakattaki sunumunuz için teşekkür ederiz.");

    private static (JobOpening Opening, JobApplication Application) Applied()
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
            new OpeningTerms(Guid.CreateVersion7(), null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Guid.CreateVersion7(), [], []), ApplicationForm.Default),
            template);
        opening.Publish(Today, At);
        var application = JobApplication.AddFromPool(opening, Candidate.Add("ayse@ornek.test", new CandidateContact("Ayşe", "Yılmaz", null, null), CandidateProfile.Empty, "REFERRAL", null), Recruiter, At).Value;
        return (opening, application);
    }

    [Fact]
    public void APersonalizedRejection_IsKeptUntilItGoes_AndDroppedOnReopen()
    {
        var (opening, application) = Applied();
        application.Reject(opening, "SKILLS", null, At.AddDays(3), Recruiter, At, Personal).IsSuccess.ShouldBeTrue();
        application.RejectionEmailText.ShouldBe(new CandidateEmailText("{position} başvurunuz", Personal.Body));

        application.Reopen(opening, null, Recruiter, At.AddHours(1)).IsSuccess.ShouldBeTrue();
        application.RejectionEmailText.ShouldBeNull();

        application.Reject(opening, "SKILLS", null, At.AddHours(2), Recruiter, At.AddHours(2), Personal).IsSuccess.ShouldBeTrue();
        application.RejectionEmailSent(At.AddHours(2));
        application.ShouldSatisfyAllConditions(a => a.RejectionEmailText.ShouldBeNull(), a => a.RejectionEmailSentAt.ShouldBe(At.AddHours(2)));
    }

    [Fact]
    public void WithoutAnEmail_OrOnAnonymization_NoTextIsKept()
    {
        var (opening, application) = Applied();
        application.Reject(opening, "SKILLS", null, null, Recruiter, At, Personal).IsSuccess.ShouldBeTrue();
        application.RejectionEmailText.ShouldBeNull();

        var (other, waiting) = Applied();
        waiting.Reject(other, "SKILLS", null, At.AddDays(1), Recruiter, At, Personal).IsSuccess.ShouldBeTrue();
        waiting.Anonymize();
        waiting.RejectionEmailText.ShouldBeNull();
    }

    [Theory]
    [InlineData(CandidateEmailKind.Rejection, "Sayın {candidate}, {position} için {company} adına teşekkürler.", true)]
    [InlineData(CandidateEmailKind.Rejection, "Mülakat {date} tarihindeydi.", false)]
    [InlineData(CandidateEmailKind.InterviewInvitation, "{date} {time}, {location}", true)]
    [InlineData(CandidateEmailKind.InterviewInvitation, "Sayın {interviewer}", false)]
    [InlineData(CandidateEmailKind.InterviewInvitation, "Süslü parantez { serbest } metindir.", true)]
    public void AText_UsesOnlyThePlaceholdersOfItsEmail(CandidateEmailKind kind, string text, bool valid) =>
        CandidateEmails.UsesKnownPlaceholders(text, kind).ShouldBe(valid);

    [Fact]
    public void TheDraftInput_GivesTheTemplateAndTheContext_WithoutTheCandidate()
    {
        var input = CandidateEmailPrompt.BuildInput(
            CandidateEmailKind.InterviewInvitation,
            "{position} pozisyonu için mülakat daveti",
            "Sayın {candidate}, sizi {date} {time} tarihinde {location} mülakata davet ediyoruz.",
            CandidateEmails.Placeholders(CandidateEmailKind.InterviewInvitation),
            new CandidateEmailContext("Satış Uzmanı", Internal: false, "Ön eleme", 1, InterviewType.Video),
            "Ekibi tanıtacağımızı belirtin.",
            "en-US");

        input.ShouldSatisfyAllConditions(
            i => i.ShouldStartWith("Output language: English"),
            i => i.ShouldContain("<email_kind>InterviewInvitation</email_kind>"),
            i => i.ShouldContain("<placeholders>{candidate}, {position}, {company}, {date}, {time}, {location}</placeholders>"),
            i => i.ShouldContain("<body>Sayın {candidate}, sizi {date} {time} tarihinde {location} mülakata davet ediyoruz.</body>"),
            i => i.ShouldContain("<stage_reached>Ön eleme</stage_reached>"),
            i => i.ShouldContain("<interview_type>Video</interview_type>"),
            i => i.ShouldContain("Ekibi tanıtacağımızı belirtin."));
    }
}
