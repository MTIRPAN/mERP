using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.SharedKernel.ValueObjects;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Openings;

/// <summary>Job openings (PLAN R3a, D49): drafted, published, held and closed; from an approved requisition or alone.</summary>
public sealed class OpeningTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/openings";

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Openings.Read,
        RecruitmentPermissions.Openings.Manage,
        "platform.companies.read",
        "platform.companies.manage",
        "platform.branches.read",
        "platform.branches.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOpening_IsDraftedWithCleanTextsAndTheDefaultStages_ThenPublishedHeldAndClosed()
    {
        var (company, branch, manager) = await SetupAsync();
        var recruiter = Recruiter();
        var body = Body(company, manager) with
        {
            BranchId = branch,
            Description = "<p>Merhaba <strong>dünya</strong><script>alert(1)</script></p><p><a href=\"javascript:alert(1)\">x</a> <a href=\"https://merp.example\">site</a></p><img src=x onerror=alert(1)>",
            Questions =
            [
                new OpeningQuestionInput(null, "Askerlik görevinizi tamamladınız mı?", QuestionType.YesNo, true, null, [OpeningQuestion.Yes]),
                new OpeningQuestionInput(null, "Hangi şehirde çalışmak istersiniz?", QuestionType.SingleChoice, false, ["İstanbul", "Ankara"], null),
            ],
        };

        var id = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, body, Ct));

        var draft = await DetailsAsync(recruiter, id);
        draft.ShouldSatisfyAllConditions(
            d => d.Opening.Status.ShouldBe(OpeningStatus.Draft),
            d => d.Opening.BranchName.ShouldBe("Gebze Fabrika"),
            d => d.Opening.HiringManagerName.ShouldNotBeNull(),
            d => d.Description.ShouldBe("<p>Merhaba <strong>dünya</strong></p><p><a>x</a> <a href=\"https://merp.example\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">site</a></p>"),
            d => d.Qualifications.ShouldBeNull(),
            d => d.Stages[0].Type.ShouldBe(StageType.Applied),
            d => d.Stages.Select(s => s.Type).ShouldContain(StageType.Hired),
            d => d.Form.Questions.Count.ShouldBe(2),
            d => d.Form.Questions[0].IsKnockout.ShouldBeTrue(),
            d => d.Form.Questions[0].Id.ShouldNotBe(Guid.Empty),
            d => d.CanPublish.ShouldBeTrue(),
            d => d.CanChangeStages.ShouldBeTrue());

        (await recruiter.PostAsync($"{Url}/{id}/publish", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var published = await DetailsAsync(recruiter, id);
        published.Opening.Status.ShouldBe(OpeningStatus.Published);
        published.Opening.PublishFrom.ShouldNotBeNull();
        published.CanChangeStages.ShouldBeFalse();

        // The stages are fixed once published
        var templates = await recruiter.GetFromJsonAsync<List<OpeningTemplateOption>>($"{Url}/stage-templates", Ct);
        var other = await OtherTemplateAsync(templates!);
        var changed = await recruiter.PutAsJsonAsync($"{Url}/{id}", body with { Version = published.Version, StageTemplateId = other }, Ct);
        changed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(changed)).ShouldContainKey("stageTemplateId");

        (await recruiter.PostAsync($"{Url}/{id}/hold", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailsAsync(recruiter, id)).Opening.Status.ShouldBe(OpeningStatus.OnHold);
        (await recruiter.PostAsync($"{Url}/{id}/hold", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await recruiter.PostAsync($"{Url}/{id}/publish", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await recruiter.PostAsJsonAsync($"{Url}/{id}/close", new { reason = "Pozisyon dolduruldu" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var closed = await DetailsAsync(recruiter, id);
        closed.ShouldSatisfyAllConditions(
            d => d.Opening.Status.ShouldBe(OpeningStatus.Closed),
            d => d.CloseReason.ShouldBe("Pozisyon dolduruldu"),
            d => d.CanEdit.ShouldBeFalse());
        (await recruiter.PutAsJsonAsync($"{Url}/{id}", body with { Version = closed.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AnOpeningOfARequisition_SeeksAtMostThePeopleLeft_AndIsListedWithIt()
    {
        var (company, _, manager) = await SetupAsync();
        var recruiter = Recruiter();
        var approved = await RequisitionAsync(company, manager, approved: true);
        var draft = await RequisitionAsync(company, manager, approved: false);

        var tooMany = await recruiter.PostAsJsonAsync(Url, Body(company, manager) with { RequisitionId = approved.Id, Headcount = 3 }, Ct);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(tooMany))["headcount"].ShouldBe(["İlandaki kişi sayısı talepte kalan kişi sayısını (2) aşamaz."]);

        var notApproved = await recruiter.PostAsJsonAsync(Url, Body(company, manager) with { RequisitionId = draft.Id }, Ct);
        (await ProblemAsync(notApproved)).ShouldContainKey("requisitionId");

        var id = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(company, manager) with { RequisitionId = approved.Id, Headcount = 2 }, Ct));
        var list = await recruiter.GetFromJsonAsync<PagedResult<OpeningListItem>>($"{Url}?requisitionId={approved.Id}", Ct);
        var row = list!.Items.ShouldHaveSingleItem();
        row.Id.ShouldBe(id);
        row.RequisitionNumber.ShouldBe(approved.Number);

        // A cancelled requisition takes no published opening any more
        await ChangeRequisitionAsync(approved.Id, r => r.Cancel("Bütçe kesildi", DateTimeOffset.UtcNow));
        var publish = await recruiter.PostAsync($"{Url}/{id}/publish", null, Ct);
        publish.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(publish)).ShouldContainKey("requisitionId");
    }

    [Fact]
    public async Task InvalidFields_Return400()
    {
        var (company, _, manager) = await SetupAsync();
        var recruiter = Recruiter();
        var (otherCompany, otherBranch, _) = await SetupAsync();

        async Task<Dictionary<string, string[]>> ErrorsAsync(OpeningBody body)
        {
            var response = await recruiter.PostAsJsonAsync(Url, body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
            return await ProblemAsync(response);
        }

        (await ErrorsAsync(Body(company, manager) with { Title = "", Headcount = 0 })).Keys.ShouldBe(["title", "headcount"], ignoreOrder: true);
        (await ErrorsAsync(Body(company, manager) with { BranchId = otherBranch })).ShouldContainKey("branchId");
        (await ErrorsAsync(Body(company, manager) with { PublishFrom = new DateOnly(2026, 12, 1), PublishUntil = new DateOnly(2026, 11, 1) })).ShouldContainKey("publishUntil");
        (await ErrorsAsync(Body(company, manager) with { HiringManagerUserId = Guid.CreateVersion7() })).ShouldContainKey("hiringManagerUserId");
        (await ErrorsAsync(Body(company, manager) with
        {
            Questions = [new OpeningQuestionInput(null, "Tek seçenek", QuestionType.SingleChoice, true, ["Evet"], null)],
        })).ShouldContainKey("questions[0]");
        (await ErrorsAsync(Body(company, manager) with
        {
            Questions = [new OpeningQuestionInput(null, "Kaç yıl?", QuestionType.Number, true, null, ["5"])],
        })).ShouldContainKey("questions[0]");
        otherCompany.ShouldNotBe(company);
    }

    [Fact]
    public async Task OpeningsOutsideTheScope_AreNotFound_AndReadersCannotManage()
    {
        var (company, _, manager) = await SetupAsync();
        var (otherCompany, _, _) = await SetupAsync();
        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(company, manager), Ct));

        var scoped = factory.CreateClientFor("scoped-recruiter", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(otherCompany));
        (await scoped.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PostAsync($"{Url}/{id}/publish", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetFromJsonAsync<PagedResult<OpeningListItem>>(Url, Ct))!.Items.ShouldNotContain(o => o.Id == id);
        (await scoped.PostAsJsonAsync(Url, Body(company, manager), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var reader = factory.CreateClientFor("opening-reader", RequisitionFactory.Tenant, RecruitmentPermissions.Openings.Read);
        (await reader.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsync($"{Url}/{id}/publish", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync(Url, Body(company, manager), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>The form's body; Version is used when updating.</summary>
    public sealed record OpeningBody(
        uint Version,
        Guid CompanyId,
        Guid? RequisitionId,
        string Title,
        string? Description,
        string? Qualifications,
        Guid? BranchId,
        WorkModel WorkModel,
        RequisitionEmploymentType EmploymentType,
        int Headcount,
        OpeningVisibility Visibility,
        DateOnly? PublishFrom,
        DateOnly? PublishUntil,
        Guid? StageTemplateId,
        Guid HiringManagerUserId,
        IReadOnlyList<Guid> RecruiterUserIds,
        IReadOnlyList<Guid> InterviewerUserIds,
        bool RequirePhone,
        bool RequireCv,
        bool AskCoverLetter,
        IReadOnlyList<OpeningQuestionInput> Questions);

    private static OpeningBody Body(Guid company, Guid manager) => new(
        0,
        company,
        null,
        "Satış Uzmanı",
        "<p>Satış ekibimize katılacak arkadaş arıyoruz.</p>",
        null,
        null,
        WorkModel.Hybrid,
        RequisitionEmploymentType.Indefinite,
        1,
        OpeningVisibility.Public,
        null,
        null,
        null,
        manager,
        [],
        [manager],
        true,
        true,
        false,
        []);

    private HttpClient Recruiter() => factory.CreateClientFor("recruiter", RequisitionFactory.Tenant, Permissions);

    /// <summary>A company with a branch, and a user who hires for it.</summary>
    private async Task<(Guid Company, Guid Branch, Guid Manager)> SetupAsync()
    {
        var admin = Recruiter();
        var company = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
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
        var branch = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/platform/branches",
            new { companyId = company, code = NewCode("B"), name = "Gebze Fabrika", type = "Factory", address = new { line1 = "OSB 3. Cad. No: 5", provinceCode = 41, districtCode = 1338 } },
            Ct));
        var manager = await UserIdAsync(factory.CreateClientFor("hiring-manager", RequisitionFactory.Tenant));
        return (company, branch, manager);
    }

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/platform/me", Ct));
        return me.RootElement.GetProperty("user").GetProperty("id").GetGuid();
    }

    /// <summary>A requisition for a new position of two people, approved or a draft; the approval flow is R2's tests.</summary>
    private async Task<(Guid Id, string Number)> RequisitionAsync(Guid company, Guid manager, bool approved)
    {
        var requisition = JobRequisition.Draft(
            NewCode("PT-"),
            manager,
            new RequisitionTerms(company, Guid.CreateVersion7(), null, "Satış Uzmanı", null, 2, RequisitionReason.NewHeadcount, null, RequisitionEmploymentType.Indefinite, null, null, null, null, "Büyüme", manager));
        if (approved)
        {
            requisition.Submit(DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            requisition.Complete(approved: true, DateTimeOffset.UtcNow);
        }

        await WithDbAsync(async db =>
        {
            db.JobRequisitions.Add(requisition);
            await db.SaveChangesAsync(Ct);
        });
        return (requisition.Id.Value, requisition.Number);
    }

    private Task ChangeRequisitionAsync(Guid id, Action<JobRequisition> change) =>
        WithDbAsync(async db =>
        {
            change(await db.JobRequisitions.FindAsync([JobRequisitionId.From(id)], Ct) ?? throw new InvalidOperationException("No requisition."));
            await db.SaveChangesAsync(Ct);
        });

    private async Task WithDbAsync(Func<RecruitmentDbContext, Task> work)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }

    /// <summary>An active template other than the default; created when there is none.</summary>
    private async Task<Guid> OtherTemplateAsync(IReadOnlyList<OpeningTemplateOption> templates)
    {
        if (templates.FirstOrDefault(t => !t.IsDefault) is { } other)
        {
            return other.Id;
        }

        var template = StageTemplate.Create(
            LocalizedString.Of("tr-TR", "Hızlı işe alım"),
            [
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Başvuru"), StageType.Applied, 3),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "İşe alındı"), StageType.Hired, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Reddedildi"), StageType.Rejected, null),
                new Stage(Guid.Empty, LocalizedString.Of("tr-TR", "Geri çekildi"), StageType.Withdrawn, null),
            ],
            isDefault: false).Value;
        await WithDbAsync(async db =>
        {
            db.StageTemplates.Add(template);
            await db.SaveChangesAsync(Ct);
        });
        return template.Id.Value;
    }

    private static async Task<OpeningDetails> DetailsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<OpeningDetails>($"{Url}/{id}", Ct))!;

    private static async Task<Dictionary<string, string[]>> ProblemAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
}
