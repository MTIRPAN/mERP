using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Audit;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.IntegrationTests.Access;
using Merp.Modules.Platform.IntegrationTests.Organization;

namespace Merp.Modules.Platform.IntegrationTests.Audit;

public sealed class AuditEndpointsTests(AccessFactory factory) : IClassFixture<AccessFactory>
{
    private const string Url = "/api/platform/audit";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>PLAN P3 acceptance: after a company update its history shows the old and new value per field.</summary>
    [Fact]
    public async Task CompanyUpdate_ShowsOldAndNewValuePerField_InTheHistory()
    {
        var admin = factory.AsRoot();
        var rootId = await factory.UserIdAsync(AccessFactory.Root);
        var id = await OrganizationFactory.CreateCompanyAsync(admin);
        var company = (await admin.GetFromJsonAsync<CompanyDetails>($"/api/platform/companies/{id}", Ct))!;
        var update = new UpdateCompanyCommand(
            company.Version,
            company.Title,
            "Acme Yeni",
            company.TaxOffice,
            company.TaxNumber,
            company.MersisNumber,
            company.SgkWorkplaceNumber,
            new(company.Address!.Line1, ProvinceCode: 6, DistrictCode: 1231),
            company.Phone,
            company.Email,
            company.Culture,
            company.TimeZoneId,
            IsActive: true);
        (await admin.PutAsJsonAsync($"/api/platform/companies/{id}", update, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var history = (await admin.GetFromJsonAsync<PagedResult<AuditItem>>($"{Url}?module=platform&entityType=Company&entityId={id}&pageSize=200", Ct))!;

        var shortName = history.Items.Single(i => i is { Action: "Update", Field: "ShortName" });
        shortName.OldValue.ShouldBe("Acme");
        shortName.NewValue.ShouldBe("Acme Yeni");
        shortName.FieldLabel.ShouldBe("Kısa ad");
        shortName.EntityLabel.ShouldBe("Şirket");
        shortName.UserId.ShouldBe(rootId);
        shortName.UserName.ShouldNotBeNullOrWhiteSpace();
        var city = history.Items.Single(i => i is { Action: "Update", Field: "Address.City" });
        (city.OldValue, city.NewValue, city.FieldLabel).ShouldBe(("İstanbul", "Ankara", "İl"));
        history.Items.Single(i => i is { Action: "Insert", Field: "Id" }).FieldLabel.ShouldBe("Kimlik");

        // Newest first: the update comes before the creation
        history.Items[0].Action.ShouldBe("Update");
        history.Items[^1].Action.ShouldBe("Insert");
        history.Items.ShouldAllBe(i => i.EntityId == id.ToString());
        history.TotalCount.ShouldBe(history.Items.Count);
    }

    [Fact]
    public async Task Search_FiltersByUserAndPeriod_AndPages()
    {
        var (client, userId) = await factory.UserWithAsync([PlatformPermissions.Companies.Read, PlatformPermissions.Companies.Manage]);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await OrganizationFactory.CreateCompanyAsync(client);
        await OrganizationFactory.CreateCompanyAsync(client);

        var byUser = (await factory.AsRoot().GetFromJsonAsync<PagedResult<AuditItem>>($"{Url}?userId={userId}&pageSize=5", Ct))!;
        byUser.Items.Count.ShouldBe(5);
        byUser.TotalCount.ShouldBeGreaterThan(10);
        byUser.Items.ShouldAllBe(i => i.UserId == userId && i.EntityType == "Company");
        byUser.Items.Select(i => i.OccurredAt).ShouldBeInOrder(SortDirection.Descending);

        var second = (await factory.AsRoot().GetFromJsonAsync<PagedResult<AuditItem>>($"{Url}?userId={userId}&pageSize=5&page=2", Ct))!;
        second.Items.Select(i => i.Id).ShouldNotContain(byUser.Items[0].Id);

        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        (await factory.AsRoot().GetFromJsonAsync<PagedResult<AuditItem>>($"{Url}?userId={userId}&from={future}", Ct))!.TotalCount.ShouldBe(0);
        var since = Uri.EscapeDataString(before.ToString("O"));
        (await factory.AsRoot().GetFromJsonAsync<PagedResult<AuditItem>>($"{Url}?userId={userId}&from={since}", Ct))!.TotalCount.ShouldBe(byUser.TotalCount);
    }

    [Fact]
    public async Task Search_WithInvalidInput_Returns400()
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var range = await factory.AsRoot().GetAsync($"{Url}?from={from}&to={to}", Ct);
        range.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(range)).Errors!.ShouldContainKey("to");

        var deep = await factory.AsRoot().GetAsync($"{Url}?page=51&pageSize=200", Ct);
        deep.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(deep)).Errors!["page"].ShouldBe(["Bu kadar eski kayıtlara sayfalayarak ulaşılamaz. Tarih aralığını ya da kaydı daraltın."]);
    }

    [Fact]
    public async Task Audit_NeedsThePermission_WithTheTenantScope()
    {
        var (withoutPermission, _) = await factory.UserWithAsync([PlatformPermissions.Companies.Read]);
        (await withoutPermission.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await withoutPermission.GetAsync($"{Url}/entity-types", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Entries carry no company, so a company scope does not let one read them (D27)
        var companyId = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var (companyScoped, _) = await factory.UserWithAsync([PlatformPermissions.Audit.Read], "Company", companyId);
        var response = await companyScoped.GetAsync($"{Url}?entityType=Company&entityId={companyId}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.audit.errors.tenant_scope_required");
    }

    [Fact]
    public async Task EntityTypes_ListTheAuditedTypesOfEveryModule_WithLabels()
    {
        var types = (await factory.AsRoot().GetFromJsonAsync<List<AuditEntityType>>($"{Url}/entity-types", Ct))!;

        types.ShouldContain(new AuditEntityType("platform", "Company", "Şirket"));
        types.ShouldContain(new AuditEntityType("platform", "RoleAssignment", "Rol ataması"));
        // A table every module has, labeled once for all (D31)
        types.ShouldContain(new AuditEntityType("platform", "StoredFile", "Dosya"));
        types.Where(t => t.Module == "platform").Select(t => t.EntityType)
            .ShouldBe(["Branch", "Company", "Culture", "CultureText", "CustomFieldDefinition", "NumberSequence", "Role", "RoleAssignment", "RolePermission", "StoredFile", "User"], ignoreOrder: true);
    }

    [Fact]
    public async Task SystemAdministrator_HoldsTheAuditPermission()
    {
        var role = await factory.SystemRoleAsync("system-admin");
        var details = (await factory.AsRoot().GetFromJsonAsync<Application.Roles.RoleDetails>($"/api/platform/roles/{role.Id}", Ct))!;

        details.Permissions.ShouldContain(PlatformPermissions.Audit.Read);
    }
}
