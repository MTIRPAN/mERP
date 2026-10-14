using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Imports;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Imports;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.IntegrationTests.Imports;

public sealed class ImportEndpointsTests(ImportFactory factory) : IClassFixture<ImportFactory>
{
    private const string TemplateUrl = "/api/platform/imports/platform.branches/template";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Definitions_ListOnlyTheImportsTheUserMayUse()
    {
        var imports = (await factory.AsRoot().GetFromJsonAsync<List<ImportDefinitionItem>>("/api/platform/imports", Ct))!;
        imports.ShouldContain(new ImportDefinitionItem("platform.branches", "Şubeler"));

        var (reader, _) = await factory.UserWithAsync([PlatformPermissions.Branches.Read]);
        (await reader.GetFromJsonAsync<List<ImportDefinitionItem>>("/api/platform/imports", Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Template_HasLocalizedHeaders_HintsAndChoiceLists()
    {
        var response = await factory.AsRoot().GetAsync(TemplateUrl, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ImportFile.ContentType);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        var sheet = workbook.Worksheet(1);
        sheet.Name.ShouldBe("Şubeler");
        Enumerable.Range(1, ImportFactory.BranchHeaders.Length).Select(c => sheet.Cell(1, c).GetString()).ShouldBe(ImportFactory.BranchHeaders);
        sheet.Cell(1, 1).GetComment().Text.ShouldContain("şirketin kodu");
        sheet.DataValidations.ShouldHaveSingleItem().Ranges.ShouldContain(r => r.FirstCell().Address.ToString() == "D2");
        var lists = workbook.Worksheet("Lists");
        lists.Visibility.ShouldBe(XLWorksheetVisibility.Hidden);
        lists.Column(4).CellsUsed().Select(c => c.GetString()).ShouldBe(["Merkez", "Fabrika", "Depo", "Mağaza", "Ofis"], ignoreOrder: true);
    }

    [Fact]
    public async Task Template_OfAnUnknownImport_Returns404_AndWithoutPermission403()
    {
        (await factory.AsRoot().GetAsync("/api/platform/imports/platform.unknown/template", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (reader, _) = await factory.UserWithAsync([PlatformPermissions.Branches.Read]);
        (await reader.GetAsync(TemplateUrl, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ImportFactory.UploadAsync(reader, ImportFactory.Workbook([]))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidFile_IsReady_ThenCommitCreatesTheBranches()
    {
        var root = factory.AsRoot();
        var (companyId, company) = await CompanyAsync();

        var job = await ImportFactory.StartAsync(root, ImportFactory.Workbook(
        [
            ImportFactory.BranchRow(company, "gbz-01", "Merkez"),
            ImportFactory.BranchRow(company.ToLowerInvariant(), "KDK-01", "Ofis", province: "34", district: "KADIKÖY"),
            ImportFactory.BranchRow(company, "IZM-01", "Depo", province: "izmir", district: "Bornova"),
        ]));

        job.Status.ShouldBe(ImportStatus.Ready, string.Join(" | ", job.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}")));
        job.TotalRows.ShouldBe(3);
        job.ImportName.ShouldBe("Şubeler");
        (await factory.QueryAsync(db => db.Branches.CountAsync(b => b.CompanyId == CompanyId.From(companyId), Ct))).ShouldBe(0);

        (await ImportFactory.CommitAsync(root, job.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        job = await ImportFactory.JobAsync(root, job.Id);
        job.Status.ShouldBe(ImportStatus.Completed);
        job.CommittedRows.ShouldBe(3);
        job.CompletedAt.ShouldNotBeNull();
        var branches = await factory.QueryAsync(db => db.Branches.AsNoTracking().Where(b => b.CompanyId == CompanyId.From(companyId)).OrderBy(b => b.Code)
            .Select(b => new { b.Code, b.Type, b.Address.ProvinceCode, b.Address.DistrictCode }).ToListAsync(Ct));
        branches.Select(b => b.Code).ShouldBe(["GBZ-01", "IZM-01", "KDK-01"]);
        branches[0].Type.ShouldBe(BranchType.HeadOffice);
        branches[0].ProvinceCode.ShouldBe(41);
        branches[2].ProvinceCode.ShouldBe(34);
        branches[2].DistrictCode.ShouldNotBeNull();
        (await factory.QueryAsync(db => db.OutboxMessages.CountAsync(m => m.Type == typeof(BranchCreated).FullName, Ct))).ShouldBeGreaterThanOrEqualTo(3);

        // A committed job is not committed again
        (await ImportFactory.CommitAsync(root, job.Id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvalidFile_ReportsEachErrorWithRowAndColumn_AndTheReportMarksTheCells()
    {
        var root = factory.AsRoot();
        var (_, company) = await CompanyAsync();

        var job = await ImportFactory.StartAsync(root, ImportFactory.Workbook(
        [
            ImportFactory.BranchRow(company, "A1"),
            ImportFactory.BranchRow("NOPE", "A2"),
            ImportFactory.BranchRow(company, "A1"),
            ImportFactory.BranchRow(company, "A4", type: "Kale"),
            ImportFactory.BranchRow(company, "A5", province: "Atlantis"),
            [company, "A6", null, "Fabrika", "Kocaeli", "Gebze", "Cad. 1", null, null, null],
            [company, "A7", "Şube A7", "Fabrika", "Kocaeli", "Gebze", "Cad. 1", null, "123", null],
        ]));

        job.Status.ShouldBe(ImportStatus.Invalid);
        job.ErrorRows.ShouldBe(6);
        job.Errors.ShouldContain(new ImportJobError(3, "companyCode", "Şirket bulunamadı."));
        job.Errors.ShouldContain(new ImportJobError(4, "code", "Bu değer dosyada birden fazla satırda var."));
        job.Errors.ShouldContain(e => e.Row == 5 && e.Column == "type" && e.Message.StartsWith("Listedeki değerlerden birini seçin: Merkez,", StringComparison.Ordinal));
        job.Errors.ShouldContain(new ImportJobError(6, "province", "İl bulunamadı."));
        job.Errors.ShouldContain(new ImportJobError(7, "name", "Bu alan zorunludur."));
        job.Errors.ShouldContain(e => e.Row == 8 && e.Column == "phone");
        (await ImportFactory.CommitAsync(root, job.Id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var report = await root.GetAsync($"/api/platform/imports/jobs/{job.Id}/report", Ct);
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("subeler-hatalar.xlsx");
        using var workbook = new XLWorkbook(await report.Content.ReadAsStreamAsync(Ct));
        var sheet = workbook.Worksheet(1);
        var errorsColumn = ImportFactory.BranchHeaders.Length + 1;
        sheet.Cell(1, errorsColumn).GetString().ShouldBe("Hatalar");
        sheet.Cell(2, errorsColumn).GetString().ShouldBeEmpty();
        sheet.Cell(3, 2).GetString().ShouldBe("A2");
        sheet.Cell(3, 1).GetComment().Text.ShouldContain("Şirket bulunamadı.");
        sheet.Cell(3, 1).Style.Fill.BackgroundColor.ShouldBe(XLColor.FromHtml("#FDE2E1"));
        sheet.Cell(3, errorsColumn).GetString().ShouldContain("Şirket bulunamadı.");
        sheet.Cell(2, 1).HasComment.ShouldBeFalse();
    }

    [Fact]
    public async Task MissingColumnOrNotASpreadsheet_IsRejected()
    {
        var root = factory.AsRoot();

        var job = await ImportFactory.StartAsync(root, ImportFactory.Workbook([["X", "Y"]], headers: ["Şirket kodu", "Şube kodu"]));
        job.Status.ShouldBe(ImportStatus.Invalid);
        job.Errors.ShouldContain(new ImportJobError(1, "name", "\"Şube adı\" sütunu dosyada yok. Lütfen indirdiğiniz şablonu kullanın."));

        var response = await ImportFactory.UploadAsync(root, "not a workbook"u8.ToArray());
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!.ShouldContainKey("file");
    }

    [Fact]
    public async Task AnotherUsersJob_IsNotFound()
    {
        var (_, company) = await CompanyAsync();
        var job = await ImportFactory.StartAsync(factory.AsRoot(), ImportFactory.Workbook([ImportFactory.BranchRow(company, "X1")]));

        var (other, _) = await factory.UserWithAsync([PlatformPermissions.Branches.Manage]);
        (await other.GetAsync($"/api/platform/imports/jobs/{job.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/platform/imports/jobs/{job.Id}/report", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ImportFactory.CommitAsync(other, job.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<List<ImportJobDetails>>("/api/platform/imports/jobs", Ct)).ShouldBeEmpty();
        (await factory.AsRoot().GetFromJsonAsync<List<ImportJobDetails>>("/api/platform/imports/jobs?key=platform.branches", Ct))!
            .ShouldContain(j => j.Id == job.Id);
    }

    [Fact]
    public async Task LargeFile_IsValidatedInTheBackground_WithTheUsersDataScope()
    {
        var (own, ownCode) = await CompanyAsync();
        var (_, otherCode) = await CompanyAsync();
        var (client, _) = await factory.UserWithAsync([PlatformPermissions.Branches.Manage], "Company", own);
        var rows = Enumerable.Range(1, ImportFactory.BackgroundThreshold + 5).Select(i => ImportFactory.BranchRow(ownCode, $"BG{i}")).ToList();
        rows.Add(ImportFactory.BranchRow(otherCode, "BG-OUT"));

        var job = await ImportFactory.StartAsync(client, ImportFactory.Workbook(rows));

        job.Status.ShouldBe(ImportStatus.Invalid);
        job.Errors.ShouldHaveSingleItem().ShouldBe(new ImportJobError(rows.Count + 1, "companyCode", "Bu şirkete şube ekleme yetkiniz yok."));
    }

    [Fact]
    public async Task FiveThousandRows_AreValidatedInUnderAMinute_AndCommittedInBatches()
    {
        var root = factory.AsRoot();
        var (companyId, company) = await CompanyAsync();
        var rows = Enumerable.Range(1, 5000).Select(i => ImportFactory.BranchRow(company, $"N{i:D5}")).ToList();
        var watch = Stopwatch.StartNew();

        // PLAN P11 acceptance: a file of 5,000 rows is validated in under a minute (upload included)
        var job = await ImportFactory.StartAsync(root, ImportFactory.Workbook(rows));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromMinutes(1));
        job.Status.ShouldBe(ImportStatus.Ready);

        (await ImportFactory.CommitAsync(root, job.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ImportFactory.JobAsync(root, job.Id)).CommittedRows.ShouldBe(5000);
        // 100 branches per transaction: the batch command is exempt from "one aggregate per command" (D35)
        (await factory.QueryAsync(db => db.Branches.CountAsync(b => b.CompanyId == CompanyId.From(companyId), Ct))).ShouldBe(5000);
    }

    [Fact]
    public async Task ABatchThatFailsAtCommit_RollsBackAlone()
    {
        var root = factory.AsRoot();
        var (companyId, company) = await CompanyAsync();
        var rows = Enumerable.Range(1, 150).Select(i => ImportFactory.BranchRow(company, $"P{i:D3}")).ToList();
        var job = await ImportFactory.StartAsync(root, ImportFactory.Workbook(rows));
        job.Status.ShouldBe(ImportStatus.Ready);

        // Someone adds row 120's branch by hand between validation and commit
        (await root.PostAsJsonAsync("/api/platform/branches", OrganizationFactory.BranchBody(companyId, "P120"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ImportFactory.CommitAsync(root, job.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        job = await ImportFactory.JobAsync(root, job.Id);
        job.Status.ShouldBe(ImportStatus.PartiallyCompleted);
        job.CommittedRows.ShouldBe(100);
        job.ErrorRows.ShouldBe(50);
        job.Errors.ShouldContain(new ImportJobError(121, "code", "Bu değer başka bir kayıtta kullanılıyor."));
        job.Errors.Count(e => e.Message.StartsWith("Aynı partideki", StringComparison.Ordinal)).ShouldBe(49);
        (await factory.QueryAsync(db => db.Branches.CountAsync(b => b.CompanyId == CompanyId.From(companyId), Ct))).ShouldBe(101);
    }

    private async Task<(Guid Id, string Code)> CompanyAsync()
    {
        var code = OrganizationFactory.NewCode();
        return (await OrganizationFactory.CreateCompanyAsync(factory.AsRoot(), OrganizationFactory.CompanyBody(code)), code);
    }
}
