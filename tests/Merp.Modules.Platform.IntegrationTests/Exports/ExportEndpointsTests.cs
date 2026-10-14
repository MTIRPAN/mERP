using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Modules.Platform.IntegrationTests.Access;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Exports;

/// <summary>Real permission checks and data scopes; an export may have at most <see cref="MaxRows"/> rows.</summary>
public sealed class ExportFactory(PostgresFixture postgres) : AccessFactory(postgres)
{
    public const int MaxRows = 5;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Exports:MaxRows"] = MaxRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public async Task<T> QueryAsync<T>(Func<PlatformDbContext, Task<T>> query)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<PlatformDbContext>());
    }
}

public sealed class ExportEndpointsTests(ExportFactory factory) : IClassFixture<ExportFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Branches_HaveTheChosenColumns_TheListFilterAndSort_AndTheExportIsAudited()
    {
        var root = factory.AsRoot();
        var company = await OrganizationFactory.CreateCompanyAsync(root);
        await CreateBranchAsync(root, company, "B2");
        await CreateBranchAsync(root, company, "B1", BranchType.HeadOffice);

        var response = await root.GetAsync($"/api/platform/branches/export?companyId={company}&sort=code&columns=code,type,isActive,actions", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldStartWith("Şubeler-");
        var sheet = await SheetAsync(response);
        Row(sheet, 1).ShouldBe(["Kod", "Tür", "Durum"]);
        Row(sheet, 2).ShouldBe(["B1", "Merkez", "Aktif"]);
        Row(sheet, 3).ShouldBe(["B2", "Fabrika", "Aktif"]);
        sheet.RangeUsed()!.RowCount().ShouldBe(3);

        var audit = await factory.QueryAsync(db => db.AuditEntries.AsNoTracking()
            .Where(e => e.Action == AuditAction.Export && e.EntityType == nameof(Branch))
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync(Ct));
        using var record = JsonDocument.Parse(audit.NewValue!);
        record.RootElement.GetProperty("rows").GetInt32().ShouldBe(2);
        record.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ShouldBe(["code", "type", "isActive"]);
        record.RootElement.GetProperty("filters").GetProperty("companyId").GetString().ShouldBe(company.ToString());
    }

    [Fact]
    public async Task Branches_OutsideTheDataScope_AreNotExported()
    {
        var root = factory.AsRoot();
        var own = await OrganizationFactory.CreateCompanyAsync(root);
        var other = await OrganizationFactory.CreateCompanyAsync(root);
        await CreateBranchAsync(root, own, "OWN1");
        await CreateBranchAsync(root, other, "OTHER1");
        var (scoped, _) = await factory.UserWithAsync([PlatformPermissions.Branches.Read], "Company", own);

        var response = await scoped.GetAsync("/api/platform/branches/export?columns=code", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sheet = await SheetAsync(response);
        Enumerable.Range(2, sheet.RangeUsed()!.RowCount() - 1).Select(r => sheet.Cell(r, 1).GetString()).ShouldBe(["OWN1"]);
    }

    [Fact]
    public async Task Export_WithoutPermission_Returns403_AndWithAnInvalidSort400()
    {
        var (noAccess, _) = await factory.UserWithAsync([PlatformPermissions.Companies.Read]);
        (await noAccess.GetAsync("/api/platform/branches/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await noAccess.GetAsync("/api/platform/users/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var response = await factory.AsRoot().GetAsync("/api/platform/branches/export?sort=nope", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!.ShouldContainKey("sort");
    }

    [Fact]
    public async Task Export_OfMoreRowsThanAllowed_AsksToNarrowTheFilter()
    {
        var root = factory.AsRoot();
        var company = await OrganizationFactory.CreateCompanyAsync(root);
        for (var i = 0; i <= ExportFactory.MaxRows; i++)
        {
            await CreateBranchAsync(root, company, $"M{i}");
        }

        var response = await root.GetAsync($"/api/platform/branches/export?companyId={company}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["filter"]
            .ShouldBe([$"Liste {ExportFactory.MaxRows + 1} satır; dışa aktarmada en fazla {ExportFactory.MaxRows} satır olabilir. Lütfen filtreyi daraltın."]);
    }

    [Fact]
    public async Task Companies_AreExportedWithTheirFilter()
    {
        var root = factory.AsRoot();
        var code = OrganizationFactory.NewCode();
        await OrganizationFactory.CreateCompanyAsync(root, OrganizationFactory.CompanyBody(code));

        var response = await root.GetAsync($"/api/platform/companies/export?search={code}&columns=code,shortName,taxNumber", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var sheet = await SheetAsync(response);
        Row(sheet, 1).ShouldBe(["Kod", "Kısa ad", "Vergi kimlik no"]);
        sheet.RangeUsed()!.RowCount().ShouldBe(2);
        sheet.Cell(2, 1).GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Users_AreExportedWithTheirRoles()
    {
        await factory.UserIdAsync(AccessFactory.Root);

        var response = await factory.AsRoot().GetAsync("/api/platform/users/export?columns=userName,roles,isActive", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var sheet = await SheetAsync(response);
        Row(sheet, 1).ShouldBe(["Kullanıcı adı", "Roller", "Durum"]);
        Enumerable.Range(2, sheet.RangeUsed()!.RowCount() - 1).Select(r => sheet.Cell(r, 2).GetString()).ShouldContain(roles => roles.Contains("Sistem Yöneticisi"));
    }

    private static async Task CreateBranchAsync(HttpClient client, Guid companyId, string code, BranchType type = BranchType.Factory)
    {
        var response = await client.PostAsJsonAsync("/api/platform/branches", OrganizationFactory.BranchBody(companyId, code, type), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<IXLWorksheet> SheetAsync(HttpResponseMessage response)
    {
        var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        return workbook.Worksheet(1);
    }

    private static string[] Row(IXLWorksheet sheet, int row) =>
        [.. Enumerable.Range(1, sheet.RangeUsed()!.ColumnCount()).Select(c => sheet.Cell(row, c).GetString())];
}
