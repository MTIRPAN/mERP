using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Numbering;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Numbering;
using Merp.Modules.Platform.IntegrationTests.Access;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.SharedKernel.Numbering;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Numbering;

/// <summary>Number patterns (PLAN P5, D29). Companies' own patterns are added in globex only.</summary>
public sealed class NumberingEndpointsTests(AccessFactory factory) : IClassFixture<AccessFactory>
{
    private const string Url = "/api/platform/numbering/sequences";
    private const string Tenant = "globex";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => PlatformClock.Today(TimeProvider.System);

    private HttpClient Root => factory.AsRoot(Tenant);

    [Fact]
    public async Task ModuleSequences_AreSeeded_WithLabelsAndExamples()
    {
        var sequences = (await Root.GetFromJsonAsync<List<NumberSequenceItem>>(Url, Ct))!;

        var offer = sequences.Single(s => s.DocumentType == RecruitmentDocumentTypes.Offer && s.CompanyId == null);
        (offer.Label, offer.Pattern, offer.ResetPeriod).ShouldBe(("İş teklifi", "TKL-{yyyy}-{seq:5}", NumberResetPeriod.Yearly));
        offer.Example.ShouldBe($"TKL-{Today.Year}-00001");
        sequences.ShouldContain(s => s.DocumentType == RecruitmentDocumentTypes.Requisition && s.Label == "Personel talebi");
    }

    [Fact]
    public async Task CompanyPattern_IsUsedForThatCompany_OthersKeepTheDefault()
    {
        var acme = await OrganizationFactory.CreateCompanyAsync(Root);
        var other = await OrganizationFactory.CreateCompanyAsync(Root);
        var created = await Root.PostAsJsonAsync(
            Url,
            new { documentType = RecruitmentDocumentTypes.Offer, companyId = acme, pattern = "ACME-{yy}-{seq:3}", resetPeriod = "Yearly" },
            Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));

        var numbers = await NumbersAsync(
            (acme, RecruitmentDocumentTypes.Offer),
            (acme, RecruitmentDocumentTypes.Offer),
            (other, RecruitmentDocumentTypes.Offer));

        var yy = Today.ToString("yy", System.Globalization.CultureInfo.InvariantCulture);
        numbers.ShouldBe([$"ACME-{yy}-001", $"ACME-{yy}-002", $"TKL-{Today.Year}-00001"]);

        // Removing it brings the default back; the company's counter goes on
        var id = (await created.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        (await Root.DeleteAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NumbersAsync((acme, RecruitmentDocumentTypes.Offer))).ShouldBe([$"TKL-{Today.Year}-00003"]);
    }

    [Fact]
    public async Task ChangingTheDefault_AppliesToTheNextNumber_AndChecksTheVersion()
    {
        var company = await OrganizationFactory.CreateCompanyAsync(Root);
        var requisition = (await Root.GetFromJsonAsync<List<NumberSequenceItem>>(Url, Ct))!
            .Single(s => s.DocumentType == RecruitmentDocumentTypes.Requisition && s.CompanyId == null);

        var update = await Root.PutAsJsonAsync($"{Url}/{requisition.Id}", new { version = requisition.Version, pattern = "PT/{yyyy}/{MM}/{seq:4}", resetPeriod = "Monthly" }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NumbersAsync((company, RecruitmentDocumentTypes.Requisition))).ShouldBe([$"PT/{Today:yyyy}/{Today:MM}/0001"]);

        var stale = await Root.PutAsJsonAsync($"{Url}/{requisition.Id}", new { version = requisition.Version, pattern = "PT-{seq}", resetPeriod = "Never" }, Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Root.DeleteAsync($"{Url}/{requisition.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvalidInput_Returns400()
    {
        var company = await OrganizationFactory.CreateCompanyAsync(Root);

        async Task<Problem> PostAsync(object body) => await Problem.ReadAsync(await Root.PostAsJsonAsync(Url, body, Ct));

        (await PostAsync(new { documentType = RecruitmentDocumentTypes.Offer, companyId = company, pattern = "TKL-{dd}", resetPeriod = "Never" }))
            .Errors!.ShouldContainKey("pattern");
        (await PostAsync(new { documentType = RecruitmentDocumentTypes.Offer, companyId = company, pattern = "TKL-{seq}", resetPeriod = "Yearly" }))
            .Errors!["pattern"].ShouldBe(["Sayaç yıllık sıfırlanıyorsa desende yıl ({yyyy} veya {yy}), aylık sıfırlanıyorsa yıl ve ay ({MM}) olmalı; yoksa numaralar tekrar eder."]);
        (await PostAsync(new { documentType = "recruitment.rocket", companyId = company, pattern = "R-{seq}", resetPeriod = "Never" }))
            .Errors!.ShouldContainKey("documentType");
        (await PostAsync(new { documentType = RecruitmentDocumentTypes.Offer, companyId = Guid.NewGuid(), pattern = "R-{seq}", resetPeriod = "Never" }))
            .Errors!.ShouldContainKey("companyId");

        var body = new { documentType = RecruitmentDocumentTypes.Requisition, companyId = company, pattern = "R-{seq}", resetPeriod = "Never" };
        (await Root.PostAsJsonAsync(Url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(body)).Errors!.ShouldContainKey("companyId");
    }

    [Fact]
    public async Task Numbering_NeedsThePermission_WithTheTenantScope()
    {
        var (reader, _) = await factory.UserWithAsync([PlatformPermissions.Companies.Read]);
        (await reader.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var companyId = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var (companyScoped, _) = await factory.UserWithAsync([PlatformPermissions.Numbering.Read, PlatformPermissions.Numbering.Manage], "Company", companyId);
        var response = await companyScoped.GetAsync(Url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.numbering.errors.tenant_scope_required");
    }

    /// <summary>Takes numbers as a Recruitment command would: in an open transaction of the Recruitment module.</summary>
    private async Task<List<string>> NumbersAsync(params (Guid Company, string DocumentType)[] requests)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, Ct))!;
        var numbers = new List<string>();
        foreach (var (company, documentType) in requests)
        {
            await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            numbers.Add(await scope.ServiceProvider.GetRequiredService<INumberGenerator>().NextAsync(company, documentType, Today, Ct));
            await transaction.CommitAsync(Ct);
        }

        return numbers;
    }
}
