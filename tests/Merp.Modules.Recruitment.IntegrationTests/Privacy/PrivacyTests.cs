using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Candidates;
using Merp.Modules.Recruitment.Application.Privacy;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Privacy;

/// <summary>
/// KVKK (PLAN R8, D55): the nightly retention anonymizes expired candidates and removes their files without changing
/// the recruitment figures, asks for the consent renewal before, and data subject requests export or erase a candidate.
/// </summary>
public sealed class PrivacyTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnExpiredCandidate_IsAnonymizedAtNight_TheFilesGo_AndTheFiguresStay()
    {
        // PLAN R8 acceptance: an expired candidate is anonymized by the nightly job and their files are deleted
        var opening = await OpeningAsync();
        var (application, candidate) = await ApplyAsync(opening, withCv: true);
        var (active, activeCandidate) = await ApplyAsync(opening, withCv: false);
        (await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{application}/reject", new { version = await ApplicationVersionAsync(application), reasonCode = "SKILLS", sendEmail = false, emailDelayDays = 0 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await FilesOfAsync(candidate)).ShouldBe(1);
        var before = await FiguresAsync(opening);

        // 25 months later; the candidate with an application in the pipeline waits
        await AgeAsync(candidate, months: 25);
        await AgeAsync(activeCandidate, months: 25);
        (await RetentionAsync()).Anonymized.ShouldBeGreaterThanOrEqualTo(1);

        var card = await CardAsync(candidate);
        card.ShouldSatisfyAllConditions(
            c => c.FirstName.ShouldBe(Candidate.AnonymousFirstName),
            c => c.LastName.ShouldStartWith("#"),
            c => c.Email.ShouldEndWith("@anonim.invalid"),
            c => c.Phone.ShouldBeNull(),
            c => c.AnonymizedAt.ShouldNotBeNull(),
            c => c.Files.ShouldBeEmpty(),
            c => c.CanManage.ShouldBeTrue(),
            c => c.CanManagePrivacy.ShouldBeFalse());
        (await FilesOfAsync(candidate)).ShouldBe(0);
        (await CardAsync(activeCandidate)).AnonymizedAt.ShouldBeNull();
        (await FiguresAsync(opening)).ShouldBe(before);
        (await Recruiter().PutAsJsonAsync($"/api/recruitment/candidates/{candidate}", new { version = card.Version, email = "yeni@ornek.test", firstName = "Ayşe", lastName = "Yılmaz", source = "CAREER_PAGE" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        active.ShouldNotBe(application);
    }

    [Fact]
    public async Task ACandidate_IsAskedToRenewBeforeTheEnd_AndTheRenewalStartsTheRetentionAgain()
    {
        var opening = await OpeningAsync();
        var (application, candidate) = await ApplyAsync(opening, withCv: false);
        (await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{application}/reject", new { version = await ApplicationVersionAsync(application), reasonCode = "SKILLS", sendEmail = false, emailDelayDays = 0 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await AgeAsync(candidate, months: 24, days: -10);

        (await RetentionAsync()).RenewalsRequested.ShouldBeGreaterThanOrEqualTo(1);
        var email = await EmailOfAsync(candidate);
        var renewal = (await factory.OutboxAsync<ExternalEmailRequested>()).Single(e => e.Type == RecruitmentNotificationTypes.ConsentRenewal && e.To == email);
        renewal.Link.ShouldNotBeNull().ShouldStartWith($"/careers/{RequisitionFactory.Tenant}/consents/");
        var link = $"/api/public/{RequisitionFactory.Tenant}/consents/{renewal.Link!.Split('/')[^1]}";

        var anonymous = factory.CreateClient();
        var page = (await anonymous.GetFromJsonAsync<ConsentRenewalPage>(link, Ct))!;
        page.ShouldSatisfyAllConditions(p => p.FirstName.ShouldBe("Ayşe"), p => p.Text.ShouldNotBeNullOrWhiteSpace());
        (await anonymous.PostAsJsonAsync(link, new { accepted = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync(link, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var card = await CardAsync(candidate);
        card.ShouldSatisfyAllConditions(
            c => c.PoolConsentAt.ShouldNotBeNull(),
            c => c.Consents.ShouldContain(x => x.Kind == CandidateConsentKind.Renewed),
            c => c.RetentionEndsOn.ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(24)));

        // A second night neither asks again nor anonymizes
        await RetentionAsync();
        (await CardAsync(candidate)).AnonymizedAt.ShouldBeNull();
        (await factory.OutboxAsync<ExternalEmailRequested>()).Count(e => e.Type == RecruitmentNotificationTypes.ConsentRenewal && e.To == email).ShouldBe(1);
    }

    [Fact]
    public async Task ADataSubjectRequest_ExportsTheData_AndErasesTheCandidate()
    {
        var opening = await OpeningAsync();
        var (application, candidate) = await ApplyAsync(opening, withCv: true);
        var privacy = factory.CreateClientFor("kvkk-sorumlusu", RequisitionFactory.Tenant, RecruitmentPermissions.Candidates.Read, RecruitmentPermissions.Candidates.Privacy, RecruitmentPermissions.Applications.Read);
        (await Recruiter().GetAsync($"/api/recruitment/candidates/{candidate}/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var export = await privacy.GetAsync($"/api/recruitment/candidates/{candidate}/export", Ct);
        export.StatusCode.ShouldBe(HttpStatusCode.OK, await export.Content.ReadAsStringAsync(Ct));
        export.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        using (var zip = new ZipArchive(await export.Content.ReadAsStreamAsync(Ct), ZipArchiveMode.Read))
        {
            zip.Entries.Select(e => e.FullName).ShouldBe(["aday.json", "dosyalar/ozgecmis.pdf"], ignoreOrder: true);
            using var reader = new StreamReader(zip.GetEntry("aday.json")!.Open());
            var json = await reader.ReadToEndAsync(Ct);
            json.ShouldContain(await EmailOfAsync(candidate));
            json.ShouldContain("Satış Uzmanı");
        }

        var card = await CardAsync(candidate, privacy);
        card.CanManagePrivacy.ShouldBeTrue();
        (await Recruiter().PostAsJsonAsync($"/api/recruitment/candidates/{candidate}/erase", new { version = card.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await privacy.PostAsJsonAsync($"/api/recruitment/candidates/{candidate}/erase", new { version = card.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var erased = await CardAsync(candidate, privacy);
        erased.ShouldSatisfyAllConditions(c => c.AnonymizedAt.ShouldNotBeNull(), c => c.Files.ShouldBeEmpty(), c => c.CanManagePrivacy.ShouldBeFalse());
        erased.Applications.ShouldHaveSingleItem().Application.Status.ShouldBe(ApplicationStatus.Withdrawn);
        (await FilesOfAsync(candidate)).ShouldBe(0);
        (await privacy.PostAsJsonAsync($"/api/recruitment/candidates/{candidate}/erase", new { version = erased.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        application.ShouldNotBe(Guid.Empty);
    }

    private HttpClient Recruiter() => factory.CreateClientFor(
        "kvkk-ise-alim",
        RequisitionFactory.Tenant,
        RecruitmentPermissions.Applications.Read,
        RecruitmentPermissions.Applications.Manage,
        RecruitmentPermissions.Candidates.Read,
        RecruitmentPermissions.Candidates.Manage);

    private async Task<CandidateDetails> CardAsync(Guid candidate, HttpClient? client = null)
    {
        var response = await (client ?? Recruiter()).GetAsync($"/api/recruitment/candidates/{candidate}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CandidateDetails>(Ct))!;
    }

    private async Task<uint> ApplicationVersionAsync(Guid application)
    {
        using var details = JsonDocument.Parse(await Recruiter().GetStringAsync($"/api/recruitment/applications/{application}", Ct));
        return details.RootElement.GetProperty("version").GetUInt32();
    }

    /// <summary>A published opening of a company of the tenant: the career page takes applications only for those.</summary>
    private async Task<Guid> OpeningAsync()
    {
        var admin = factory.CreateClientFor("kvkk-sirket-yoneticisi", RequisitionFactory.Tenant, "platform.companies.read", "platform.companies.manage");
        var company = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/platform/companies",
            new
            {
                code = "C" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                title = "Acme Holding Anonim Şirketi",
                shortName = "Acme",
                taxOffice = "Kadıköy",
                taxNumber = TurkishData.Vkn(),
                address = new { line1 = "Atatürk Cad. No: 1", provinceCode = 34, districtCode = 1421, postalCode = "34710" },
            },
            Ct));
        return await PublishAsync(company);
    }

    private async Task<Guid> PublishAsync(Guid company) => await WithDbAsync(async db =>
    {
        var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
        var opening = JobOpening.Draft(
            new OpeningTerms(company, null, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(Guid.CreateVersion7(), [], []), new ApplicationForm(false, false, false, [])),
            template);
        opening.Publish(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        db.JobOpenings.Add(opening);
        await db.SaveChangesAsync(Ct);
        return opening.Id.Value;
    });

    private async Task<(Guid Application, Guid Candidate)> ApplyAsync(Guid opening, bool withCv)
    {
        var email = $"kvkk.{Guid.NewGuid().ToString("N")[..12]}@ornek.test";
        var body = new { firstName = "Ayşe", lastName = "Yılmaz", email, phone = "0532 123 45 67", disclosureAccepted = true, disclosureRevision = 0, poolConsent = false, culture = "tr-TR" };
        using var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8), "application" } };
        if (withCv)
        {
            var cv = new ByteArrayContent("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());
            cv.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(cv, "cv", "ozgecmis.pdf");
        }

        var response = await factory.CreateClient().PostAsync($"/api/public/{RequisitionFactory.Tenant}/careers/openings/{opening}/applications", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return await WithDbAsync(async db =>
        {
            var row = await db.JobApplications
                .Where(a => a.OpeningId == JobOpeningId.From(opening))
                .Join(db.Candidates.Where(c => c.Email == email), a => a.CandidateId, c => c.Id, (a, c) => new { Application = a.Id.Value, Candidate = c.Id.Value })
                .SingleAsync(Ct);
            return (row.Application, row.Candidate);
        });
    }

    /// <summary>Moves the candidate's dates back as if they had applied that long ago.</summary>
    private Task<int> AgeAsync(Guid candidate, int months, int days = 0)
    {
        var at = DateTimeOffset.UtcNow.AddMonths(-months).AddDays(-days);
        return WithDbAsync(db => db.Candidates.Where(c => c.Id == CandidateId.From(candidate))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, at).SetProperty(c => c.LastAppliedAt, at), Ct));
    }

    private Task<int> FilesOfAsync(Guid candidate) =>
        WithDbAsync(db => db.StoredFiles.CountAsync(f => f.OwnerType == "recruitment.candidate" && f.OwnerId == candidate, Ct));

    private Task<string> EmailOfAsync(Guid candidate) =>
        WithDbAsync(db => db.Candidates.Where(c => c.Id == CandidateId.From(candidate)).Select(c => c.Email).SingleAsync(Ct));

    /// <summary>What the recruitment reports count of an opening: its applications by status and source.</summary>
    private Task<string> FiguresAsync(Guid opening) =>
        WithDbAsync(async db => string.Join(
            ',',
            (await db.JobApplications.Where(a => a.OpeningId == JobOpeningId.From(opening)).Select(a => new { a.Status, a.Source }).ToListAsync(Ct))
                .OrderBy(a => a.Status).ThenBy(a => a.Source, StringComparer.Ordinal)
                .Select(a => $"{a.Status}:{a.Source}")));

    private async Task<CandidateRetentionResult> RetentionAsync()
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunCandidateRetentionCommand(), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<T> WithDbAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }
}
