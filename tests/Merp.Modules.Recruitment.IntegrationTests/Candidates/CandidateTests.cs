using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Candidates;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.SharedKernel.ValueObjects;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Merp.Modules.Recruitment.IntegrationTests.Candidates;

/// <summary>
/// The candidate pool (PLAN R4b, D50): one candidate per e-mail with duplicates suggested and merged, the card, notes
/// with mentions, CVs and their text in the full text search, adding to another opening; the hiring team's access.
/// </summary>
public sealed class CandidateTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/candidates";

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Candidates.Read,
        RecruitmentPermissions.Candidates.Manage,
        RecruitmentPermissions.Applications.Read,
        RecruitmentPermissions.Applications.Manage,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSameEmail_IsNotASecondCandidate_AndTheSamePhoneIsSuggestedForAMerge()
    {
        // PLAN R4 acceptance: no second candidate with the same e-mail; a merge is suggested
        var recruiter = Recruiter();
        var email = NewEmail();
        var digits = Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var first = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(email) with { Phone = $"0532 {digits}" }, Ct));

        var again = await recruiter.PostAsJsonAsync(Url, Body(email.ToUpperInvariant()), Ct);
        (await ProblemAsync(again)).ShouldContainKey("email");

        var second = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()) with { FirstName = "Ayşe Nur", Phone = $"+90 532{digits}" }, Ct));
        var card = await CardAsync(recruiter, first);
        card.Duplicates.ShouldHaveSingleItem().ShouldSatisfyAllConditions(d => d.Id.ShouldBe(second), d => d.SamePhone.ShouldBeTrue(), d => d.SameName.ShouldBeFalse());

        // Changing the e-mail to another candidate's is refused too
        var update = await recruiter.PutAsJsonAsync($"{Url}/{second}", UpdateBody(await CardAsync(recruiter, second)) with { Email = email }, Ct);
        (await ProblemAsync(update)).ShouldContainKey("email");

        (await ProblemAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()) with { LinkedInUrl = "javascript:alert(1)" }, Ct))).ShouldContainKey("linkedInUrl");
        (await ProblemAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()) with { Tags = ["NOPE"] }, Ct))).ShouldContainKey("tags");
        (await ProblemAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()) with { Source = "NOPE" }, Ct))).ShouldContainKey("source");
        (await factory.CreateClientFor("reader", RequisitionFactory.Tenant, RecruitmentPermissions.Candidates.Read).PostAsJsonAsync(Url, Body(NewEmail()), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ADuplicate_IsMerged_WithItsApplicationsCvsAndNotes()
    {
        var (company, manager, _) = await SetupAsync();
        var firstOpening = await OpeningAsync(company, manager);
        var secondOpening = await OpeningAsync(company, manager);
        var recruiter = Recruiter();
        var target = await ApplyAsync(firstOpening, NewEmail());
        var duplicate = await ApplyAsync(secondOpening, NewEmail(), withCv: true);
        await NoteAsync(recruiter, duplicate, "Telefonla görüşüldü");

        var merged = await recruiter.PostAsJsonAsync($"{Url}/{target}/merge", new { version = (await CardAsync(recruiter, target)).Version, duplicateId = duplicate }, Ct);
        merged.StatusCode.ShouldBe(HttpStatusCode.NoContent, await merged.Content.ReadAsStringAsync(Ct));

        var card = await CardAsync(recruiter, target);
        card.Applications.Select(a => a.Application.OpeningId).ShouldBe([firstOpening, secondOpening], ignoreOrder: true);
        card.Files.ShouldHaveSingleItem().IsLatest.ShouldBeTrue();
        card.Notes.ShouldHaveSingleItem().Body.ShouldBe("Telefonla görüşüldü");
        (await recruiter.GetAsync($"{Url}/{duplicate}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Two applications to one opening cannot become one candidate's
        var third = await ApplyAsync(firstOpening, NewEmail());
        var refused = await recruiter.PostAsJsonAsync($"{Url}/{target}/merge", new { version = (await CardAsync(recruiter, target)).Version, duplicateId = third }, Ct);
        (await ProblemAsync(refused)).ShouldContainKey("duplicateId");
        (await ProblemAsync(await recruiter.PostAsJsonAsync($"{Url}/{target}/merge", new { version = (await CardAsync(recruiter, target)).Version, duplicateId = target }, Ct)))
            .ShouldContainKey("duplicateId");
    }

    [Fact]
    public async Task TheCard_ShowsApplicationsInScope_NotesWithMentions_AndTheTimeline()
    {
        var (company, manager, _) = await SetupAsync();
        var (otherCompany, _, _) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var recruiter = Recruiter();
        var candidate = await ApplyAsync(opening, NewEmail());
        var colleague = await UserIdAsync(factory.CreateClientFor($"colleague-{Guid.NewGuid():N}", RequisitionFactory.Tenant));

        var tag = await TagAsync();
        var updated = await recruiter.PutAsJsonAsync($"{Url}/{candidate}", UpdateBody(await CardAsync(recruiter, candidate)) with
        {
            CurrentTitle = "Bölge Satış Sorumlusu",
            ExperienceYears = 6.5m,
            Tags = [tag],
            Source = "LINKEDIN",
        }, Ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.NoContent, await updated.Content.ReadAsStringAsync(Ct));
        var note = await NoteAsync(recruiter, candidate, "Mülakata çağıralım", colleague);

        var card = await CardAsync(recruiter, candidate);
        card.ShouldSatisfyAllConditions(
            c => c.Profile.CurrentTitle.ShouldBe("Bölge Satış Sorumlusu"),
            c => c.Profile.ExperienceYears.ShouldBe(6.5m),
            c => c.Tags.ShouldHaveSingleItem().Name.ShouldBe("Satış"),
            c => c.SourceName.ShouldBe("LinkedIn"),
            c => c.Applications.ShouldHaveSingleItem().Consent.ShouldNotBeNull(),
            c => c.Notes.ShouldHaveSingleItem().ShouldSatisfyAllConditions(n => n.CanRemove.ShouldBeTrue(), n => n.Mentioned.ShouldHaveSingleItem().UserId.ShouldBe(colleague)),
            c => c.Timeline.Select(t => t.Kind).ShouldBe([CandidateTimelineKind.Note, CandidateTimelineKind.Applied]),
            c => c.CanManage.ShouldBeTrue());
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n =>
            n.Type == CandidateNotificationTypes.Mentioned && n.RecipientUserIds.Contains(colleague) && n.Link == $"/recruitment/candidates/{candidate}");

        // Another company's recruiter sees the candidate but not the application; only the author removes a note
        var scoped = factory.CreateClientFor("scoped-recruiter", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(otherCompany));
        (await CardAsync(scoped, candidate)).Applications.ShouldBeEmpty();
        (await scoped.DeleteAsync($"{Url}/{candidate}/notes/{note}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await recruiter.DeleteAsync($"{Url}/{candidate}/notes/{note}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await recruiter.GetAsync($"{Url}/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClientFor("nobody", RequisitionFactory.Tenant).GetAsync($"{Url}/{candidate}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProblemAsync(await recruiter.PostAsJsonAsync($"{Url}/{candidate}/notes", new { body = "" }, Ct))).ShouldContainKey("body");
    }

    [Fact]
    public async Task ThePoolSearch_FindsTheProfileNotesTagsAndCvText()
    {
        var recruiter = Recruiter();
        var word = $"zx{Guid.NewGuid().ToString("N")[..8]}";
        var noted = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()), Ct));
        await NoteAsync(recruiter, noted, $"Muhasebe yazılımı {word} biliyor");
        var withCv = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()) with { CurrentCompany = "Kuzey Lojistik" }, Ct));
        var cvWord = $"cv{Guid.NewGuid().ToString("N")[..8]}";
        var uploaded = await UploadAsync(recruiter, withCv, ("ozgecmis.pdf", "application/pdf", Pdf($"Experience with {cvWord} and Excel")));
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync(Ct));
        var tag = await TagAsync();
        var tagged = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()) with { Tags = [tag] }, Ct));

        (await SearchAsync(recruiter, $"search={word}")).ShouldHaveSingleItem().Id.ShouldBe(noted);
        (await SearchAsync(recruiter, "search=Kuzey Lojistik")).ShouldContain(c => c.Id == withCv);
        (await SearchAsync(recruiter, $"search={cvWord}")).ShouldBeEmpty();
        (await SendAsync(new IndexCandidateCvsCommand())).ShouldBeGreaterThan(0);
        (await SearchAsync(recruiter, $"search={cvWord}")).ShouldHaveSingleItem().Id.ShouldBe(withCv);
        (await SearchAsync(recruiter, $"tag={tag}")).ShouldContain(c => c.Id == tagged);
        (await SearchAsync(recruiter, $"tag={tag}")).ShouldNotContain(c => c.Id == noted);

        var card = await CardAsync(recruiter, withCv);
        var file = card.Files.ShouldHaveSingleItem();
        (await recruiter.GetAsync($"{Url}/{withCv}/cvs/{file.Id}", Ct)).Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await ProblemAsync(await UploadAsync(recruiter, withCv, ("cv.txt", "text/plain", "Özgeçmiş"u8.ToArray())))).ShouldContainKey("cv");
        (await recruiter.GetAsync($"{Url}?sort=email", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ACandidate_IsAddedToAnotherOpening_OfACompanyInScope()
    {
        var (company, manager, _) = await SetupAsync();
        var (otherCompany, _, _) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var draft = await OpeningAsync(company, manager, publish: false);
        var recruiter = Recruiter();
        var candidate = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(NewEmail()), Ct));

        var added = await recruiter.PostAsJsonAsync($"{Url}/{candidate}/applications", new { openingId = opening }, Ct);
        var application = await RequisitionFactory.CreatedIdAsync(added);
        var details = (await recruiter.GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{application}", Ct))!;
        details.ShouldSatisfyAllConditions(d => d.Consent.ShouldBeNull(), d => d.Application.Source.ShouldBe("REFERRAL"), d => d.History.ShouldHaveSingleItem());

        (await ProblemAsync(await recruiter.PostAsJsonAsync($"{Url}/{candidate}/applications", new { openingId = opening }, Ct))).ShouldContainKey("openingId");
        (await recruiter.PostAsJsonAsync($"{Url}/{candidate}/applications", new { openingId = draft }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var scoped = factory.CreateClientFor("scoped-recruiter", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(otherCompany));
        (await ProblemAsync(await scoped.PostAsJsonAsync($"{Url}/{candidate}/applications", new { openingId = draft }, Ct))).ShouldContainKey("openingId");
        // The form's choices: active sources and tags; referrers among the employees of a company in scope
        (await recruiter.GetFromJsonAsync<CandidateOptions>($"{Url}/options", Ct))!.Sources.ShouldContain(s => s.Code == "LINKEDIN" && s.Name == "LinkedIn");
        (await recruiter.GetAsync($"{Url}/referrers?companyId={company}&search=a", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await scoped.GetAsync($"{Url}/referrers?companyId={company}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("reader", RequisitionFactory.Tenant, RecruitmentPermissions.Candidates.Manage)
            .PostAsJsonAsync($"{Url}/{candidate}/applications", new { openingId = opening }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheHiringTeam_ReadsAndNotesTheCandidatesOfItsOpenings()
    {
        var (company, manager, managerSubject) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var otherOpening = await OpeningAsync(company, (await SetupAsync()).Manager);
        var candidate = await ApplyAsync(opening, NewEmail());
        var stranger = await ApplyAsync(otherOpening, NewEmail());
        var team = factory.CreateClientFor(managerSubject, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring);

        var card = (await team.GetFromJsonAsync<CandidateDetails>($"/api/recruitment/me/hiring/candidates/{candidate}", Ct))!;
        card.ShouldSatisfyAllConditions(c => c.Applications.ShouldHaveSingleItem(), c => c.Files.ShouldBeEmpty(), c => c.CanManage.ShouldBeFalse());
        var noted = await team.PostAsJsonAsync($"/api/recruitment/me/hiring/candidates/{candidate}/notes", new { body = "Sahada denemek isterim" }, Ct);
        var note = await RequisitionFactory.CreatedIdAsync(noted);
        (await team.DeleteAsync($"/api/recruitment/me/hiring/candidates/{candidate}/notes/{note}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await team.GetAsync($"/api/recruitment/me/hiring/candidates/{stranger}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await team.PostAsJsonAsync($"/api/recruitment/me/hiring/candidates/{stranger}/notes", new { body = "?" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await team.GetAsync($"{Url}/{candidate}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await team.GetFromJsonAsync<List<JsonElement>>("/api/recruitment/me/hiring/mention-users?search=a", Ct)).ShouldNotBeNull();
    }

    private HttpClient Recruiter() => factory.CreateClientFor("pool-recruiter", RequisitionFactory.Tenant, Permissions);

    private static string NewEmail() => $"aday.{Guid.NewGuid().ToString("N")[..12]}@ornek.test";

    /// <summary>A candidate with a last name of its own, so only the duplicates a test makes are suggested.</summary>
    private static CandidateBody Body(string email) =>
        new(email, "Ayşe", $"Yılmaz {Guid.NewGuid().ToString("N")[..8]}", null, "REFERRAL", null, "İstanbul", null, null, null, null, null, null, null);

    private static CandidateBody UpdateBody(CandidateDetails card) => new(
        card.Email,
        card.FirstName,
        card.LastName,
        card.Phone,
        card.Source,
        card.ReferrerEmployeeId,
        card.Profile.Location,
        card.Profile.CurrentTitle,
        card.Profile.CurrentCompany,
        card.Profile.ExperienceYears,
        card.Profile.Education,
        card.Profile.LinkedInUrl,
        card.Profile.PortfolioUrl,
        [.. card.Tags.Select(t => t.Code)])
    {
        Version = card.Version,
    };

    private static async Task<CandidateDetails> CardAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<CandidateDetails>($"{Url}/{id}", Ct))!;

    private static async Task<IReadOnlyList<CandidateListItem>> SearchAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<CandidateListItem>>($"{Url}?pageSize=200&{query}", Ct))!.Items;

    private static async Task<Guid> NoteAsync(HttpClient client, Guid candidate, string body, params Guid[] mentioned) =>
        await RequisitionFactory.CreatedIdAsync(await client.PostAsJsonAsync($"{Url}/{candidate}/notes", new { body, mentionedUserIds = mentioned }, Ct));

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid candidate, (string Name, string ContentType, byte[] Content) file)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        form.Add(content, "cv", file.Name);
        return await client.PostAsync($"{Url}/{candidate}/cvs", form, Ct);
    }

    private static byte[] Pdf(string line)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddText(line, 11, new PdfPoint(40, 780), builder.AddStandard14Font(Standard14Font.Helvetica));
        return builder.Build();
    }

    /// <summary>A tag of the tenant's list; created once.</summary>
    private Task<string> TagAsync() =>
        WithDbAsync(async db =>
        {
            if (!await db.Lookups.AnyAsync(l => l.Kind == LookupKind.Tag && l.Code == "SALES", Ct))
            {
                db.Lookups.Add(RecruitmentLookup.Create(LookupKind.Tag, Code.Create("SALES").Value, LocalizedString.Of("tr-TR", "Satış"), 1));
                await db.SaveChangesAsync(Ct);
            }

            return "SALES";
        });

    /// <summary>A company and a user who hires for it.</summary>
    private async Task<(Guid Company, Guid Manager, string ManagerSubject)> SetupAsync()
    {
        var company = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(
            "/api/platform/companies",
            new
            {
                code = NewCode("C"),
                title = "Acme Holding Anonim Şirketi",
                shortName = "Acme",
                taxOffice = "Kadıköy",
                taxNumber = TurkishData.Vkn(),
                address = new { line1 = "Atatürk Cad. No: 1", provinceCode = 34, districtCode = 1421, postalCode = "34710" },
            },
            Ct));
        var subject = $"manager-{Guid.NewGuid():N}";
        return (company, await UserIdAsync(factory.CreateClientFor(subject, RequisitionFactory.Tenant)), subject);
    }

    /// <summary>A public opening with the default stages, asking only name and e-mail; published unless asked otherwise.</summary>
    private Task<Guid> OpeningAsync(Guid company, Guid manager, bool publish = true) =>
        WithDbAsync(async db =>
        {
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var opening = JobOpening.Draft(
                new OpeningTerms(
                    company,
                    null,
                    "Satış Uzmanı",
                    null,
                    null,
                    null,
                    WorkModel.OnSite,
                    RequisitionEmploymentType.Indefinite,
                    1,
                    OpeningVisibility.Public,
                    null,
                    null,
                    new OpeningTeam(manager, [], []),
                    new ApplicationForm(RequirePhone: false, RequireCv: false, AskCoverLetter: false, [])),
                template);
            if (publish)
            {
                opening.Publish(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            }

            db.JobOpenings.Add(opening);
            await db.SaveChangesAsync(Ct);
            return opening.Id.Value;
        });

    /// <summary>Applies from the career page; returns the candidate.</summary>
    private async Task<Guid> ApplyAsync(Guid opening, string email, bool withCv = false)
    {
        var body = new { firstName = "Ayşe", lastName = "Yılmaz", email, disclosureAccepted = true, disclosureRevision = 0, poolConsent = false, culture = "tr-TR" };
        using var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8), "application" } };
        if (withCv)
        {
            var cv = new ByteArrayContent(Pdf("Curriculum vitae"));
            cv.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(cv, "cv", "ozgecmis.pdf");
        }

        var response = await factory.CreateClient().PostAsync($"/api/public/{RequisitionFactory.Tenant}/careers/openings/{opening}/applications", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var normalized = Candidate.NormalizeEmail(email);
        return await WithDbAsync(db => db.Candidates.Where(c => c.Email == normalized).Select(c => c.Id.Value).SingleAsync(Ct));
    }

    private async Task<int> SendAsync(IndexCandidateCvsCommand command)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Ct)).Value;
    }

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/platform/me", Ct));
        return me.RootElement.GetProperty("user").GetProperty("id").GetGuid();
    }

    private async Task<T> WithDbAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }

    private static async Task<Dictionary<string, string[]>> ProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed record CandidateBody(
        string Email,
        string FirstName,
        string LastName,
        string? Phone,
        string Source,
        Guid? ReferrerEmployeeId,
        string? Location,
        string? CurrentTitle,
        string? CurrentCompany,
        decimal? ExperienceYears,
        string? Education,
        string? LinkedInUrl,
        string? PortfolioUrl,
        IReadOnlyList<string>? Tags)
    {
        public uint Version { get; init; }
    }
}
