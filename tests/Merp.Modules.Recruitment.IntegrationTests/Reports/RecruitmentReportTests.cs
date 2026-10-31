using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Reports;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Reports;

/// <summary>
/// Recruitment reports (PLAN R9, D56): with a company's September 2026 seeded through the domain, every figure of the
/// dashboard matches the one computed by hand below.
/// </summary>
/// <remarks>
/// The opening is published on 15 August. Of the September applications A1 (referral) is hired, A2 (career page) is
/// rejected after the HR interview, A3 (career page) skips to the manager interview and declines the offer, and A4
/// (referral) withdraws at once. A5 applied in August and is hired in September.
/// </remarks>
public sealed class RecruitmentReportTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Period = "from=2026-09-01&to=2026-09-30";
    private static readonly DateTimeOffset Published = At(8, 15);
    private static readonly Guid Recruiter = Guid.CreateVersion7();
    private static readonly Guid Interviewer1 = Guid.CreateVersion7();
    private static readonly Guid Interviewer2 = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDashboard_MatchesTheFiguresComputedByHand()
    {
        var company = await SeedAsync();
        var client = Reader();
        var query = $"{Period}&companyId={company}";

        var summary = (await client.GetFromJsonAsync<RecruitmentSummary>($"/api/recruitment/reports/summary?{query}", Ct))!;
        summary.ShouldBe(new RecruitmentSummary(Applications: 4, Hires: 2, AverageDaysToHire: 20.5m, AverageDaysToFill: 32.0m, OfferAcceptanceRate: 66.7m, OpenRequisitions: 2, MissingForms: 1));

        // Reached counts every working stage up to the furthest one entered: A3 skipped the HR interview
        var funnel = await PageAsync<FunnelRow>(client, $"funnel?{query}");
        funnel.Select(r => (r.StageType, r.Reached, r.ConversionRate, r.Current)).ShouldBe(
        [
            (StageType.Applied, 4, null, 0),
            (StageType.Screening, 3, 75.0m, 0),
            (StageType.Interview, 3, 100.0m, 0),
            (StageType.Interview, 2, 66.7m, 0),
            (StageType.Offer, 2, 100.0m, 1),
            (StageType.Hired, 1, 50.0m, 1),
            (StageType.Rejected, 1, null, 1),
            (StageType.Withdrawn, 1, null, 1),
        ]);

        // A1: 2 Sep → 21 Sep, published 15 Aug; A5: 20 Aug → 11 Sep
        var hires = await PageAsync<HireRow>(client, $"hires?{query}");
        hires.Select(r => (r.CandidateName, r.DaysToHire, r.DaysToFill)).ShouldBe([("Beş Aday", 22.0m, 27.0m), ("Bir Aday", 19.0m, 37.0m)]);

        var sources = await PageAsync<SourceRow>(client, $"sources?{query}");
        sources.Select(r => (r.Source, r.Applications, r.Interviewed, r.Offered, r.Hired, r.HireRate)).ShouldBe(
        [
            ("CAREER_PAGE", 2, 1, 1, 0, 0.0m),
            ("REFERRAL", 2, 1, 1, 1, 50.0m),
        ]);

        // The cancelled interview does not count; the first interviewer sent forms 4 and 24 hours after the end
        var interviewers = await PageAsync<InterviewerRow>(client, $"interviewers?{query}");
        interviewers.Select(r => (r.UserId, r.Interviews, r.Completed, r.FormsSent, r.FormsMissing, r.AverageFeedbackHours)).ShouldBe(
        [
            (Interviewer1, 2, 2, 2, 0, 14.0m),
            (Interviewer2, 1, 1, 0, 1, null),
        ]);

        var aging = await PageAsync<RequisitionAgingRow>(client, $"requisition-aging?companyId={company}");
        aging.Select(r => (r.Status, r.AgeDays, r.AgeBucket)).ShouldBe([(RequisitionStatus.Approved, 100, "90+"), (RequisitionStatus.Pending, 45, "31-60")]);

        var offers = await PageAsync<OfferReportRow>(client, $"offers?{query}");
        offers.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            o => o.Sent.ShouldBe(3),
            o => o.Accepted.ShouldBe(2),
            o => o.Declined.ShouldBe(1),
            o => o.Expired.ShouldBe(0),
            o => o.Waiting.ShouldBe(0),
            o => o.AcceptanceRate.ShouldBe(66.7m));

        // Every report has its Excel file
        var export = await client.GetAsync($"/api/recruitment/reports/funnel/export?{query}", Ct);
        export.StatusCode.ShouldBe(HttpStatusCode.OK, await export.Content.ReadAsStringAsync(Ct));
        export.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [Fact]
    public async Task AnotherCompanysRecruiter_SeesNothing_AndOthersAreTurnedAway()
    {
        var company = await SeedAsync();
        var query = $"{Period}&companyId={company}";
        var other = Reader().WithScopes(AccessScope.Company(Guid.CreateVersion7()));

        var summary = (await other.GetFromJsonAsync<RecruitmentSummary>($"/api/recruitment/reports/summary?{query}", Ct))!;
        summary.ShouldBe(new RecruitmentSummary(0, 0, null, null, null, 0, 0));
        (await PageAsync<FunnelRow>(other, $"funnel?{query}")).ShouldBeEmpty();
        (await PageAsync<RequisitionAgingRow>(other, $"requisition-aging?companyId={company}")).ShouldBeEmpty();

        var outsider = factory.CreateClientFor("rapor-yetkisiz", RequisitionFactory.Tenant, RecruitmentPermissions.Applications.Read);
        (await outsider.GetAsync($"/api/recruitment/reports/summary?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/recruitment/reports/offers/export?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Reader().GetAsync($"/api/recruitment/reports/summary?from=2026-09-30&to=2026-09-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Reader().GetAsync($"/api/recruitment/reports/hires?from=2020-01-01&to=2026-09-30", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static DateTimeOffset At(int month, int day, int hour = 8) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.UtcDateTime);

    private static string Hash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private HttpClient Reader() => factory.CreateClientFor("ise-alim-raporlari", RequisitionFactory.Tenant, RecruitmentPermissions.Reports.Read);

    private static async Task<IReadOnlyList<T>> PageAsync<T>(HttpClient client, string report)
    {
        var response = await client.GetAsync($"/api/recruitment/reports/{report}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResult<T>>(Ct))!.Items;
    }

    /// <summary>The company's September described on the class, in a company of its own.</summary>
    private async Task<Guid> SeedAsync()
    {
        var company = Guid.CreateVersion7();
        await WithDbAsync(async db =>
        {
            var template = await db.StageTemplates.AsNoTracking().SingleAsync(t => t.IsDefault, Ct);
            var opening = JobOpening.Draft(
                new OpeningTerms(company, null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 2, OpeningVisibility.Public, null, null, new OpeningTeam(Recruiter, [], []), new ApplicationForm(false, false, false, [])),
                template);
            opening.Publish(Day(Published), Published).IsSuccess.ShouldBeTrue();
            db.JobOpenings.Add(opening);
            var stages = opening.Stages.Where(s => !s.IsFinal).Select(s => s.Id).ToList();
            var form = EvaluationForm.Check(new EvaluationForm([new EvaluationSection("Yetkinlik", [new EvaluationCriterion(Guid.Empty, "İletişim", ["1", "2", "3", "4", "5"], 1)])], [])).Value;
            var criterion = form.Criteria.Single().Id;

            JobApplication Apply(string name, string source, DateTimeOffset at)
            {
                var candidate = Candidate.Add($"rapor.{Guid.NewGuid():N}@ornek.test", new CandidateContact(name, "Aday", null, "tr-TR"), CandidateProfile.Empty, source, null);
                db.Candidates.Add(candidate);
                var application = JobApplication.AddFromPool(opening, candidate, Recruiter, at).Value;
                db.JobApplications.Add(application);
                return application;
            }

            void Move(JobApplication application, int stage, DateTimeOffset at) =>
                application.MoveTo(opening, stages[stage], null, Recruiter, at).IsSuccess.ShouldBeTrue();

            JobOffer Offer(JobApplication application, DateTimeOffset sentAt, bool accepted)
            {
                var offer = JobOffer.Draft("TKL-RPR-" + Guid.NewGuid().ToString("N")[..8], application, opening, null, Recruiter, new OfferTerms("Satış Uzmanı", null, new DateOnly(2026, 10, 15), 90_000m, "TRY", SalaryPeriod.Monthly, null, new DateOnly(2026, 9, 30))).Value;
                offer.Submit(false, Day(sentAt), sentAt.AddHours(-2)).IsSuccess.ShouldBeTrue();
                offer.Complete(true, sentAt.AddHours(-1));
                offer.Send(Guid.CreateVersion7(), Hash(), Day(sentAt), sentAt).IsSuccess.ShouldBeTrue();
                offer.Respond(accepted, null, OfferResponseChannel.Hr, Recruiter, Day(sentAt.AddDays(1)), sentAt.AddDays(1)).IsSuccess.ShouldBeTrue();
                db.JobOffers.Add(offer);
                return offer;
            }

            Interview Meet(JobApplication application, DateTimeOffset startsAt, params Guid[] interviewers)
            {
                var interview = Interview.Schedule(application, new InterviewTerms(InterviewType.InPerson, startsAt, startsAt.AddHours(1), "Merkez ofis", null, interviewers, null), form, Recruiter, startsAt.AddDays(-2)).Value;
                db.Interviews.Add(interview);
                return interview;
            }

            void Feedback(Interview interview, Guid interviewer, DateTimeOffset at) =>
                db.InterviewFeedbacks.Add(InterviewFeedback.Submit(interview, interviewer, [new CriterionScore(criterion, 4, null)], [], Recommendation.Yes, null, at).Value);

            // A1: referral, hired on 21 September after an accepted offer
            var a1 = Apply("Bir", "REFERRAL", At(9, 2));
            Move(a1, 1, At(9, 3));
            Move(a1, 2, At(9, 5));
            var i1 = Meet(a1, At(9, 6, 10), Interviewer1, Interviewer2);
            i1.Complete().IsSuccess.ShouldBeTrue();
            Feedback(i1, Interviewer1, At(9, 6, 15));
            Move(a1, 3, At(9, 8));
            Move(a1, 4, At(9, 10));
            var o1 = Offer(a1, At(9, 11), accepted: true);
            a1.Hire(opening, o1, null, Recruiter, At(9, 21)).IsSuccess.ShouldBeTrue();

            // A2: career page, rejected after the HR interview; its form came a day later
            var a2 = Apply("İki", "CAREER_PAGE", At(9, 3));
            Move(a2, 1, At(9, 4));
            Move(a2, 2, At(9, 5));
            var i2 = Meet(a2, At(9, 6, 14), Interviewer1);
            i2.Complete().IsSuccess.ShouldBeTrue();
            Feedback(i2, Interviewer1, At(9, 7, 15));
            a2.Reject(opening, "SKILLS", null, null, Recruiter, At(9, 7)).IsSuccess.ShouldBeTrue();

            // A3: career page, straight to the manager interview (cancelled), declined the offer
            var a3 = Apply("Üç", "CAREER_PAGE", At(9, 4));
            Move(a3, 3, At(9, 6));
            Meet(a3, At(9, 8, 10), Interviewer2).Cancel(null).IsSuccess.ShouldBeTrue();
            Move(a3, 4, At(9, 9));
            Offer(a3, At(9, 10), accepted: false);

            // A4: referral, withdrew the next day
            var a4 = Apply("Dört", "REFERRAL", At(9, 5));
            a4.Withdraw(opening, "OTHER_OFFER", null, Recruiter, At(9, 6)).IsSuccess.ShouldBeTrue();

            // A5: applied in August, hired on 11 September
            var a5 = Apply("Beş", "CAREER_PAGE", At(8, 20));
            Move(a5, 4, At(8, 28));
            var o5 = Offer(a5, At(9, 1), accepted: true);
            a5.Hire(opening, o5, null, Recruiter, At(9, 11)).IsSuccess.ShouldBeTrue();

            // Requisitions: waiting for 45 days, approved 100 days ago, and a draft that is not open
            var now = DateTimeOffset.UtcNow;
            foreach (var (sentDaysAgo, approved) in new (int?, bool)[] { (45, false), (100, true), (null, false) })
            {
                var requisition = JobRequisition.Draft(
                    "PT-RPR-" + Guid.NewGuid().ToString("N")[..8],
                    Recruiter,
                    new RequisitionTerms(company, Guid.CreateVersion7(), null, "Bölge Satış Müdürü", null, 1, RequisitionReason.NewHeadcount, null, RequisitionEmploymentType.Indefinite, null, null, null, null, "Yeni bölge", Recruiter));
                if (sentDaysAgo is { } days)
                {
                    requisition.Submit(now.AddDays(-days)).IsSuccess.ShouldBeTrue();
                    if (approved)
                    {
                        requisition.Complete(true, now.AddDays(-days + 3));
                    }
                }

                db.JobRequisitions.Add(requisition);
            }

            return await db.SaveChangesAsync(Ct);
        });
        return company;
    }

    private async Task<T> WithDbAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }
}
