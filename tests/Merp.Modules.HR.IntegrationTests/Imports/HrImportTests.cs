using System.Net;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Imports;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Domain.Imports;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.IntegrationTests.Imports;

/// <summary>The job catalog, units, positions and employees from Excel (PLAN H4, D40).</summary>
public sealed class HrImportTests(HrImportFactory factory) : IClassFixture<HrImportFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SampleFiles_ImportWithoutErrors_InTheirOrder()
    {
        // PLAN H4 acceptance: the sample files of samples/ are imported without errors
        var admin = factory.Admin();
        var companyId = await HrFactory.CreateCompanyAsync(admin, "DEMO");
        var folder = Path.Combine(HrImportFactory.RepositoryRoot(), "samples");
        foreach (var (key, file) in new[]
        {
            (JobImport.ImportKey, "1-unvanlar.xlsx"),
            (OrgUnitImport.ImportKey, "2-birimler.xlsx"),
            (PositionImport.ImportKey, "3-pozisyonlar.xlsx"),
            (EmployeeImport.ImportKey, "4-personel.xlsx"),
        })
        {
            var job = await HrImportFactory.StartAsync(admin, key, await File.ReadAllBytesAsync(Path.Combine(folder, file), Ct));
            job.Status.ShouldBe(ImportStatus.Ready, $"{file}: {HrImportFactory.Describe(job)}");

            job = await HrImportFactory.CommitAsync(admin, job);
            job.Status.ShouldBe(ImportStatus.Completed, $"{file}: {HrImportFactory.Describe(job)}");
            job.CommittedRows.ShouldBe(job.TotalRows);
        }

        (await factory.QueryAsync(db => db.Employees.CountAsync(e => e.Employments.Any(m => m.CompanyId == companyId), Ct))).ShouldBe(2000);
        (await factory.QueryAsync(db => db.OrgUnits.CountAsync(u => u.CompanyId == companyId && u.ManagerPositionId == null, Ct))).ShouldBe(0);
        (await factory.ReportingLinesAsync(companyId)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task InvalidTckn_IsReportedOnItsCell_AndTheOtherRowsAreImportedWhenAsked()
    {
        // PLAN H4 acceptance: a row with an invalid TCKN is in the report, the other rows are imported
        var admin = factory.Admin();
        var org = await OrganizationAsync(admin);
        var good = new[] { TurkishData.Tckn(), TurkishData.Tckn() };

        var job = await HrImportFactory.StartAsync(admin, EmployeeImport.ImportKey, HrImportFactory.Workbook(
        [
            org.EmployeeRow(good[0]),
            org.EmployeeRow("12345678901"),
            org.EmployeeRow(good[1]),
        ]));

        job.Status.ShouldBe(ImportStatus.Invalid);
        job.CanCommitValidRows.ShouldBeTrue();
        job.Errors.ShouldHaveSingleItem().ShouldBe(new ImportJobError(3, "tckn", "T.C. kimlik numarası geçersiz."));

        job = await HrImportFactory.CommitAsync(admin, job, skipInvalidRows: true);

        job.Status.ShouldBe(ImportStatus.PartiallyCompleted);
        job.CommittedRows.ShouldBe(2);
        job.Errors.ShouldContain(e => e.Row == 3 && e.Column == "tckn");
        (await factory.QueryAsync(db => db.Employees.Where(e => good.Contains(e.Tckn)).CountAsync(Ct))).ShouldBe(2);
    }

    [Fact]
    public async Task UpdateMode_UpdatesThePersonAndPrimaryEmail_ButNotTheJob()
    {
        var admin = factory.Admin();
        var org = await OrganizationAsync(admin);
        var tckn = TurkishData.Tckn();
        (await HrImportFactory.CommitAsync(admin, await HrImportFactory.StartAsync(admin, EmployeeImport.ImportKey, HrImportFactory.Workbook([org.EmployeeRow(tckn)]))))
            .Status.ShouldBe(ImportStatus.Completed);
        var row = org.EmployeeRow(tckn, position: org.OtherPosition);
        row[EmployeeImport.Columns.LastName] = "Kaya";
        row[EmployeeImport.Columns.Email] = "ayse.kaya@acme.test";
        row[EmployeeImport.Columns.BirthDate] = null;

        // Without the update mode a known TCKN is an error
        var create = await HrImportFactory.StartAsync(admin, EmployeeImport.ImportKey, HrImportFactory.Workbook([row]));
        create.Errors.ShouldHaveSingleItem().Column.ShouldBe("tckn");

        var job = await HrImportFactory.StartAsync(admin, EmployeeImport.ImportKey, HrImportFactory.Workbook([row]), "CreateOrUpdate");
        job.Status.ShouldBe(ImportStatus.Ready, HrImportFactory.Describe(job));
        job.Mode.ShouldBe(Merp.Application.Abstractions.Imports.ImportMode.CreateOrUpdate);
        job.UpdateRows.ShouldBe(1);
        (await HrImportFactory.CommitAsync(admin, job)).Status.ShouldBe(ImportStatus.Completed);

        var employee = await factory.QueryAsync(db => db.Employees.Include(e => e.Employments).ThenInclude(m => m.Assignments).SingleAsync(e => e.Tckn == tckn, Ct));
        employee.LastName.ShouldBe("Kaya");
        employee.BirthDate.ShouldBe(new DateOnly(1990, 5, 1));
        employee.Emails.Single(e => e.IsPrimary).Address.ShouldBe("ayse.kaya@acme.test");
        employee.Emails.Count.ShouldBe(2);
        employee.Employments.ShouldHaveSingleItem().Assignments.ShouldHaveSingleItem().PositionId.Value.ShouldBe(org.PositionId);
    }

    [Fact]
    public async Task UpdateMode_WithoutAnOpenEmploymentInTheRowsCompany_IsAnError()
    {
        var admin = factory.Admin();
        var org = await OrganizationAsync(admin);
        var other = await OrganizationAsync(admin);
        var tckn = TurkishData.Tckn();
        await HrImportFactory.CommitAsync(admin, await HrImportFactory.StartAsync(admin, EmployeeImport.ImportKey, HrImportFactory.Workbook([org.EmployeeRow(tckn)])));

        var job = await HrImportFactory.StartAsync(admin, EmployeeImport.ImportKey, HrImportFactory.Workbook([other.EmployeeRow(tckn)]), "CreateOrUpdate");

        job.Errors.ShouldHaveSingleItem().ShouldBe(new ImportJobError(2, "companyCode",
            "Bu T.C. kimlik numarasıyla kayıtlı kişinin bu şirkette açık istihdamı yok. Tekrar işe giriş veya grup içi nakli personel kartından yapın."));
        job.UpdateRows.ShouldBe(0);
    }

    [Fact]
    public async Task NewEmployee_NeedsTheEmploymentColumns_AndAnActivePositionOfTheCompany()
    {
        var admin = factory.Admin();
        var org = await OrganizationAsync(admin);
        var noPosition = org.EmployeeRow(TurkishData.Tckn());
        noPosition[EmployeeImport.Columns.Position] = null;
        noPosition[EmployeeImport.Columns.HireDate] = null;
        var fixedTerm = org.EmployeeRow(TurkishData.Tckn());
        fixedTerm[EmployeeImport.Columns.EmploymentType] = "Belirli süreli";

        var job = await HrImportFactory.StartAsync(admin, EmployeeImport.ImportKey, HrImportFactory.Workbook(
        [
            noPosition,
            org.EmployeeRow(TurkishData.Tckn(), position: "YOK-01"),
            fixedTerm,
        ]));

        job.Errors.ShouldContain(new ImportJobError(2, "positionCode", "Yeni personel için bu alan zorunludur."));
        job.Errors.ShouldContain(new ImportJobError(2, "hireDate", "Yeni personel için bu alan zorunludur."));
        job.Errors.ShouldContain(e => e.Row == 3 && e.Column == "positionCode");
        job.Errors.ShouldContain(e => e.Row == 4 && e.Column == "contractEndDate");
    }

    [Fact]
    public async Task OrgUnits_FindTheirParentsAbove_AndRowsBelowADroppedParentAreDroppedToo()
    {
        var admin = factory.Admin();
        var code = HrFactory.NewCode("K");
        var companyId = await HrFactory.CreateCompanyAsync(admin, code);
        Dictionary<string, string?> Unit(string unit, string? parent) => new()
        {
            [OrgUnitImport.Columns.Company] = code.ToLowerInvariant(),
            [OrgUnitImport.Columns.Code] = unit,
            [OrgUnitImport.Columns.Name] = "Birim " + unit,
            [OrgUnitImport.Columns.Type] = "Departman",
            [OrgUnitImport.Columns.Parent] = parent,
        };

        var job = await HrImportFactory.StartAsync(admin, OrgUnitImport.ImportKey, HrImportFactory.Workbook(
        [
            Unit("TOP", null),
            Unit("ALT", "TOP"),
            Unit("ONCE", "SONRA"),
            Unit("SONRA", null),
            Unit("YETIM", "YOK"),
            Unit("TORUN", "YETIM"),
        ]));

        job.Errors.ShouldContain(new ImportJobError(4, "parentCode", "Bu kod dosyada daha aşağıdaki bir satırda. Satırı, bağlı olduğu satırın altına taşıyın."));
        job.Errors.ShouldContain(e => e.Row == 6 && e.Column == "parentCode");
        job.ErrorRows.ShouldBe(2);

        job = await HrImportFactory.CommitAsync(admin, job, skipInvalidRows: true);

        // TORUN's parent YETIM was dropped, so TORUN is dropped at commit
        job.CommittedRows.ShouldBe(3);
        job.Errors.ShouldContain(e => e.Row == 7 && e.Column == "parentCode");
        var units = await factory.QueryAsync(db => db.OrgUnits.Where(u => u.CompanyId == companyId).ToListAsync(Ct));
        units.Select(u => u.Code).ShouldBe(["TOP", "ALT", "SONRA"], ignoreOrder: true);
        units.Single(u => u.Code == "ALT").ParentId.ShouldBe(units.Single(u => u.Code == "TOP").Id);
    }

    [Fact]
    public async Task Positions_ReportToRowsAbove_AndAUnitHasOneManager()
    {
        var admin = factory.Admin();
        var org = await OrganizationAsync(admin);
        Dictionary<string, string?> Position(string code, string? reportsTo, bool managesUnit) => new()
        {
            [PositionImport.Columns.Company] = org.CompanyCode,
            [PositionImport.Columns.Code] = code,
            [PositionImport.Columns.OrgUnit] = org.UnitCode,
            [PositionImport.Columns.Job] = org.JobCode,
            [PositionImport.Columns.ReportsTo] = reportsTo,
            [PositionImport.Columns.Norm] = "3",
            [PositionImport.Columns.ManagesUnit] = managesUnit ? "Evet" : "Hayır",
        };

        var job = await HrImportFactory.StartAsync(admin, PositionImport.ImportKey, HrImportFactory.Workbook(
        [
            Position("P-YON", null, managesUnit: false),
            Position("P-UZM", "P-YON", managesUnit: false),
            Position("P-IKI", null, managesUnit: true),
        ]));

        // The organization's unit already has a manager
        job.Errors.ShouldHaveSingleItem().ShouldBe(new ImportJobError(4, "managesUnit", "Birimin zaten bir yönetici pozisyonu var."));
        job = await HrImportFactory.CommitAsync(admin, job, skipInvalidRows: true);

        job.CommittedRows.ShouldBe(2);
        var positions = await factory.QueryAsync(db => db.Positions.Where(p => p.CompanyId == org.CompanyId).ToListAsync(Ct));
        positions.Single(p => p.Code == "P-UZM").ReportsToPositionId.ShouldBe(positions.Single(p => p.Code == "P-YON").Id);
        positions.Single(p => p.Code == "P-UZM").Norm.ShouldBe(3);
    }

    [Fact]
    public async Task Jobs_NeedTheTenantScope_AndDuplicatesAreReported()
    {
        var admin = factory.Admin();
        var code = HrFactory.NewCode("J");
        Dictionary<string, string?> Job(string jobCode) => new() { [JobImport.Columns.Code] = jobCode, [JobImport.Columns.Name] = "Unvan " + jobCode };

        var job = await HrImportFactory.StartAsync(admin, JobImport.ImportKey, HrImportFactory.Workbook([Job(code), Job(code.ToLowerInvariant())]));
        job.Errors.ShouldHaveSingleItem().ShouldBe(new ImportJobError(3, "code", "Bu değer dosyada birden fazla satırda var."));

        var companyId = await HrFactory.CreateCompanyAsync(admin);
        var companyScoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Manage).WithScopes(AccessScope.Company(companyId));
        var scoped = await HrImportFactory.StartAsync(companyScoped, JobImport.ImportKey, HrImportFactory.Workbook([Job(HrFactory.NewCode("J"))]));
        scoped.Status.ShouldBe(ImportStatus.Invalid);
        scoped.CanCommitValidRows.ShouldBeFalse();
        scoped.Errors.ShouldHaveSingleItem().Row.ShouldBe(0);
    }

    [Fact]
    public async Task CompanyOutsideTheScope_IsAnError_AndWithoutThePermission403()
    {
        var admin = factory.Admin();
        var own = await OrganizationAsync(admin);
        var other = await OrganizationAsync(admin);
        var client = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Create).WithScopes(AccessScope.Company(own.CompanyId));

        var job = await HrImportFactory.StartAsync(client, EmployeeImport.ImportKey, HrImportFactory.Workbook(
        [
            own.EmployeeRow(TurkishData.Tckn()),
            other.EmployeeRow(TurkishData.Tckn()),
        ]));

        job.Errors.ShouldHaveSingleItem().ShouldBe(new ImportJobError(3, "companyCode", "Bu şirketin kayıtlarını yönetme yetkiniz yok."));
        var reader = factory.CreateClientFor("okur", HrFactory.Tenant, HrPermissions.Employees.Read);
        (await HrImportFactory.UploadAsync(reader, EmployeeImport.ImportKey, HrImportFactory.Workbook([own.EmployeeRow(TurkishData.Tckn())])))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/platform/imports/{EmployeeImport.ImportKey}/template", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Template_OfEmployees_HasTheTurkishHeaders()
    {
        var response = await factory.Admin().GetAsync($"/api/platform/imports/{EmployeeImport.ImportKey}/template", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var workbook = new ClosedXML.Excel.XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        var sheet = workbook.Worksheet(1);
        sheet.Name.ShouldBe("Personel");
        sheet.Cell(1, 2).GetString().ShouldBe("T.C. kimlik no *");
    }

    /// <summary>A company with a unit (and its manager position), a job and two positions, made by importing them.</summary>
    private async Task<ImportOrganization> OrganizationAsync(HttpClient admin)
    {
        var company = HrFactory.NewCode("C");
        var companyId = await HrFactory.CreateCompanyAsync(admin, company);
        var job = HrFactory.NewCode("J");
        var unit = HrFactory.NewCode("U");
        await ImportAsync(admin, JobImport.ImportKey, new Dictionary<string, string?> { [JobImport.Columns.Code] = job, [JobImport.Columns.Name] = "Satış Uzmanı", [JobImport.Columns.Level] = "Uzman" });
        await ImportAsync(admin, OrgUnitImport.ImportKey, new Dictionary<string, string?>
        {
            [OrgUnitImport.Columns.Company] = company,
            [OrgUnitImport.Columns.Code] = unit,
            [OrgUnitImport.Columns.Name] = "Satış",
            [OrgUnitImport.Columns.Type] = "Department",
        });
        Dictionary<string, string?> Position(string code, bool managesUnit) => new()
        {
            [PositionImport.Columns.Company] = company,
            [PositionImport.Columns.Code] = code,
            [PositionImport.Columns.OrgUnit] = unit,
            [PositionImport.Columns.Job] = job,
            [PositionImport.Columns.Norm] = "5",
            [PositionImport.Columns.ManagesUnit] = managesUnit ? "1" : "0",
        };
        await ImportAsync(admin, PositionImport.ImportKey, Position("SAT-01", managesUnit: true), Position("SAT-02", managesUnit: false));
        var positionId = await factory.QueryAsync(db => db.Positions.Where(p => p.CompanyId == companyId && p.Code == "SAT-01").Select(p => p.Id.Value).SingleAsync(Ct));
        return new ImportOrganization(companyId, company, unit, job, positionId, "SAT-01", "SAT-02");
    }

    private static async Task ImportAsync(HttpClient admin, string key, params Dictionary<string, string?>[] rows)
    {
        var job = await HrImportFactory.StartAsync(admin, key, HrImportFactory.Workbook(rows));
        job.Status.ShouldBe(ImportStatus.Ready, HrImportFactory.Describe(job));
        (await HrImportFactory.CommitAsync(admin, job)).Status.ShouldBe(ImportStatus.Completed);
    }
}

/// <summary>An organization made for a test, by codes as the files name it.</summary>
internal sealed record ImportOrganization(
    Guid CompanyId,
    string CompanyCode,
    string UnitCode,
    string JobCode,
    Guid PositionId,
    string Position,
    string OtherPosition)
{
    public Dictionary<string, string?> EmployeeRow(string tckn, string? position = null) => new()
    {
        [EmployeeImport.Columns.Company] = CompanyCode,
        [EmployeeImport.Columns.Tckn] = tckn,
        [EmployeeImport.Columns.FirstName] = "Ayşe",
        [EmployeeImport.Columns.LastName] = "Yılmaz",
        [EmployeeImport.Columns.BirthDate] = "01.05.1990",
        [EmployeeImport.Columns.Gender] = "Kadın",
        [EmployeeImport.Columns.Email] = $"{tckn}@acme.test",
        [EmployeeImport.Columns.Phone] = "0532 123 45 67",
        [EmployeeImport.Columns.HireDate] = "2026-01-05",
        [EmployeeImport.Columns.EmploymentType] = "Indefinite",
        [EmployeeImport.Columns.Position] = position ?? Position,
    };
}
