using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Persistence.Interceptors;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Employees;

public sealed class EmployeeEndpointsTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private const string Url = "/api/hr/employees";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hire_CreatesThePerson_AndFillsThePosition()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var tckn = TurkishData.Tckn();

        var id = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            Url,
            setup.HireBody(tckn, extra: new { birthDate = "1990-05-01", gender = "Female", nationality = "tr", probationEndDate = "2026-03-01" }),
            Ct));

        var employee = await EmployeeSetup.LoadAsync(factory, id);
        employee.Tckn.ShouldBe(tckn);
        employee.Nationality.ShouldBe("TR");
        var employment = employee.Employments.ShouldHaveSingleItem();
        employment.CompanyId.ShouldBe(setup.CompanyId);
        employment.RegistryNumber.ShouldBe("000001");
        employment.ProbationEndDate.ShouldBe(new DateOnly(2026, 3, 1));
        var assignment = employment.Assignments.ShouldHaveSingleItem();
        assignment.PositionId.Value.ShouldBe(setup.PositionA);
        assignment.Level.ShouldBe("M2");
        assignment.Announced.ShouldBeTrue();

        // PLAN H2 acceptance: after the hire the position shows as filled
        (await EmployeeSetup.FilledAsync(admin, setup.CompanyId, setup.PositionA)).ShouldBe(1);
        var tree = (await admin.GetFromJsonAsync<OrgUnitTree>($"/api/hr/org-units/tree?companyId={setup.CompanyId}", Ct))!;
        tree.Units.Single(u => u.Id == setup.UnitId).ShouldSatisfyAllConditions(
            u => u.Filled.ShouldBe(1),
            u => u.Vacant.ShouldBe(2));

        (await factory.OutboxAsync<EmployeeHired>()).ShouldContain(e => e.EmployeeId == id && e.RegistryNumber == "000001" && e.CompanyId == setup.CompanyId);

        // The TCKN is sensitive: the audit keeps only the fact that it was set
        var audited = await factory.QueryAsync(db => db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == nameof(Employee) && e.EntityId == id.ToString() && e.Field == nameof(Employee.Tckn))
            .Select(e => e.NewValue)
            .SingleAsync(Ct));
        audited.ShouldBe(AuditLogInterceptor.MaskedValue);
    }

    [Fact]
    public async Task Hire_WithARegisteredTckn_Returns400WithAClearMessage()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var tckn = TurkishData.Tckn();
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(Url, setup.HireBody(tckn), Ct));

        var response = await admin.PostAsJsonAsync(Url, setup.HireBody(tckn, positionId: setup.PositionB), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["tckn"].ShouldBe(["Bu T.C. kimlik numarasıyla kayıtlı bir personel var. Tekrar işe girişi personelin kartından yapın."]);
    }

    [Fact]
    public async Task Hire_WithInvalidInput_Returns400WithFieldErrors()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);

        var invalid = await admin.PostAsJsonAsync(Url, setup.HireBody("12345678901", extra: new { firstName = "", employmentType = (string?)null }), Ct);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["tckn", "firstName", "employmentType"], ignoreOrder: true);

        var wrongPosition = await admin.PostAsJsonAsync(
            Url,
            setup.HireBody(positionId: other.PositionA, extra: new { seniorityDate = "2020-01-01", employmentType = "PartTime" }),
            Ct);
        (await Problem.ReadAsync(wrongPosition)).Errors!.Keys.ShouldBe(["positionId"]);

        var terms = await admin.PostAsJsonAsync(Url, setup.HireBody(extra: new { seniorityDate = "2020-01-01", employmentType = "PartTime" }), Ct);
        (await Problem.ReadAsync(terms)).Errors!.Keys.ShouldBe(["seniorityReason", "weeklyHours"], ignoreOrder: true);
    }

    [Fact]
    public async Task Hire_InACompanyOutsideTheScope_OrWithoutPermission_Returns403()
    {
        var admin = factory.Admin();
        var own = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Create).WithScopes(AccessScope.Company(own.CompanyId));

        await HrFactory.CreatedIdAsync(await scoped.PostAsJsonAsync(Url, own.HireBody(), Ct));
        (await scoped.PostAsJsonAsync(Url, other.HireBody(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var reader = factory.CreateClientFor("reader", HrFactory.Tenant, HrPermissions.Employees.Read);
        (await reader.PostAsJsonAsync(Url, own.HireBody(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EmployeesOutsideTheScope_Return404()
    {
        var admin = factory.Admin();
        var own = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var foreign = await other.HireAsync(admin);
        var employment = (await EmployeeSetup.LoadAsync(factory, foreign)).Employments[0].Id.Value;
        var version = await EmployeeSetup.VersionAsync(factory, foreign);
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrFactory.AllPermissions).WithScopes(AccessScope.Company(own.CompanyId));

        (await scoped.PutAsJsonAsync($"{Url}/{foreign}/person", new { version, firstName = "X", lastName = "Y" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PostAsJsonAsync($"{Url}/{foreign}/assignments", new { employmentId = employment, validFrom = "2026-06-01", reason = "Promotion", positionId = other.PositionB }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PostAsJsonAsync($"{Url}/{foreign}/terminate", new { employmentId = employment, date = "2026-06-30", sgkExitCode = "03", rehireable = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PutAsJsonAsync($"{Url}/{foreign}/sensitive-data", new { bloodType = "0 Rh+" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rehire_AfterTheEmploymentEnded_NumbersANewEmployment()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin);
        var first = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;
        var rehire = new { companyId = setup.CompanyId, hireDate = "2026-04-01", employmentType = "Indefinite", positionId = setup.PositionB };

        var stillOpen = await admin.PostAsJsonAsync($"{Url}/{id}/employments", rehire, Ct);
        (await Problem.ReadAsync(stillOpen)).Errors!["companyId"].ShouldBe(["Personelin bu şirkette devam eden bir istihdamı var."]);

        (await admin.PostAsJsonAsync($"{Url}/{id}/terminate", new { employmentId = first, date = "2026-02-28", sgkExitCode = "03", rehireable = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var employment = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync($"{Url}/{id}/employments", rehire, Ct));

        var employee = await EmployeeSetup.LoadAsync(factory, id);
        employee.Employments.Count.ShouldBe(2);
        var latest = employee.Employments.Single(m => m.Id.Value == employment);
        latest.RegistryNumber.ShouldBe("000002");
        latest.IsPrimary.ShouldBeTrue();
        (await factory.OutboxAsync<EmployeeRehired>()).ShouldContain(e => e.EmploymentId == employment);
        (await factory.OutboxAsync<EmployeeTerminated>()).ShouldContain(e => e.EmploymentId == first && e.SgkExitCode == "03" && !e.HasOtherEmployment);
    }

    [Fact]
    public async Task Terminate_WithAnUnknownSgkCode_Returns400()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin);
        var employment = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;

        var response = await admin.PostAsJsonAsync($"{Url}/{id}/terminate", new { employmentId = employment, date = "2026-02-28", sgkExitCode = "99", rehireable = false }, Ct);

        (await Problem.ReadAsync(response)).Errors!["sgkExitCode"].ShouldBe(["SGK çıkış kodu bulunamadı ya da pasif."]);
        (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task GroupTransfer_KeepsTheSeniority_AndOpensTheTargetEmployment()
    {
        var admin = factory.Admin();
        var source = await EmployeeSetup.CreateAsync(admin);
        var target = await EmployeeSetup.CreateAsync(admin);
        var id = await source.HireAsync(admin, extra: new { seniorityDate = "2020-01-01", seniorityReason = "Önceki grup şirketi hizmeti" });
        var employment = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;
        var body = new
        {
            employmentId = employment,
            targetCompanyId = target.CompanyId,
            transferDate = "2026-07-01",
            sgkExitCode = "16",
            employmentType = "Indefinite",
            positionId = target.PositionA,
        };

        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Update).WithScopes(AccessScope.Company(source.CompanyId));
        (await scoped.PostAsJsonAsync($"{Url}/{id}/group-transfer", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var moved = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync($"{Url}/{id}/group-transfer", body, Ct));

        var employee = await EmployeeSetup.LoadAsync(factory, id);
        var old = employee.Employments.Single(m => m.Id.Value == employment);
        old.TerminationDate.ShouldBe(new DateOnly(2026, 6, 30));
        old.SgkExitCode.ShouldBe("16");
        // PLAN H2 acceptance: the seniority date survives a transfer within the group
        var created = employee.Employments.Single(m => m.Id.Value == moved);
        created.CompanyId.ShouldBe(target.CompanyId);
        created.SeniorityDate.ShouldBe(new DateOnly(2020, 1, 1));
        created.RegistryNumber.ShouldBe("000001");
        created.IsPrimary.ShouldBeTrue();
        created.Assignments.ShouldHaveSingleItem().Reason.ShouldBe(AssignmentReason.Transfer);
        (await factory.OutboxAsync<EmployeeGroupTransferred>()).ShouldContain(e =>
            e.TargetEmploymentId == moved && e.SourceCompanyId == source.CompanyId && e.SeniorityKept && e.TransferLeaveBalance && e.CompanyId == target.CompanyId);
    }

    [Fact]
    public async Task Contacts_AreReplaced_WithTheReadVersion()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin);
        var version = await EmployeeSetup.VersionAsync(factory, id);
        var body = new
        {
            version,
            emails = new[] { new { kind = "Work", address = " Ayse.Yilmaz@Acme.com.tr ", isPrimary = true } },
            phones = new[] { new { kind = "Mobile", number = "0532 123 45 67", isPrimary = true } },
            addresses = new[] { new { kind = "Home", line = "Moda Cad. No: 3", district = "Kadıköy", city = "İstanbul" } },
            emergencyContacts = new[] { new { fullName = "Mehmet Yılmaz", relation = "Spouse", phone = "0533 765 43 21" } },
        };

        var invalid = await admin.PutAsJsonAsync($"{Url}/{id}/contacts", new { version, emails = new[] { new { kind = "Work", address = "ayse@", isPrimary = true } } }, Ct);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["emails[0].address"]);

        (await admin.PutAsJsonAsync($"{Url}/{id}/contacts", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var employee = await EmployeeSetup.LoadAsync(factory, id);
        employee.Emails.ShouldHaveSingleItem().Address.ShouldBe("ayse.yilmaz@acme.com.tr");
        employee.Phones.ShouldHaveSingleItem().IsPrimary.ShouldBeTrue();
        employee.Addresses.ShouldHaveSingleItem().CountryCode.ShouldBe("TR");
        employee.EmergencyContacts.ShouldHaveSingleItem().Relation.ShouldBe(FamilyRelation.Spouse);

        // Another change was saved since the client read the card
        (await admin.PutAsJsonAsync($"{Url}/{id}/contacts", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task FamilyAndQualifications_AreStoredOnTheCard()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin);

        (await admin.PutAsJsonAsync(
            $"{Url}/{id}/family",
            new { version = await EmployeeSetup.VersionAsync(factory, id), members = new[] { new { relation = "Child", fullName = "Ali Yılmaz", birthDate = "2018-04-23", isWorking = false } } },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync(
            $"{Url}/{id}/qualifications",
            new
            {
                version = await EmployeeSetup.VersionAsync(factory, id),
                educations = new[] { new { level = "Bachelor", school = "Boğaziçi Üniversitesi", department = "İşletme", graduationYear = 2012 } },
                certificates = new[] { new { kind = "DriverLicense", name = "B", validUntil = "2032-01-01" } },
                languages = new[] { new { code = "en", level = "C1" } },
            },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var employee = await EmployeeSetup.LoadAsync(factory, id);
        employee.FamilyMembers.ShouldHaveSingleItem().BirthDate.ShouldBe(new DateOnly(2018, 4, 23));
        employee.Educations.ShouldHaveSingleItem().School.ShouldBe("Boğaziçi Üniversitesi");
        employee.Certificates.ShouldHaveSingleItem().Kind.ShouldBe(CertificateKind.DriverLicense);
        employee.Languages.ShouldHaveSingleItem().Level.ShouldBe(LanguageLevel.C1);
        (await factory.AuditedFieldsAsync(nameof(Employee), id)).ShouldContain(nameof(Employee.FamilyMembers));
    }

    [Fact]
    public async Task SensitiveData_IsStoredEncrypted_AndNeedsItsPermission()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin);
        var body = new { disabilityDegree = 40, bloodType = "AB Rh-", healthNotes = "Gluten alerjisi", criminalRecordDate = "2026-01-10" };

        var withoutSensitive = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Update);
        (await withoutSensitive.PutAsJsonAsync($"{Url}/{id}/sensitive-data", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await admin.PutAsJsonAsync($"{Url}/{id}/sensitive-data", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var stored = await factory.QueryAsync(db => db.EmployeeSensitiveData.AsNoTracking().SingleAsync(s => s.Id == EmployeeId.From(id), Ct));
        stored.HealthNotes.ShouldNotBeNull();
        stored.HealthNotes.ShouldNotContain("Gluten");
        var plain = await factory.InTenantAsync(services =>
        {
            var protector = services.GetRequiredService<ISensitiveDataProtector>();
            return Task.FromResult((protector.Unprotect(stored.DisabilityDegree), protector.Unprotect(stored.BloodType), protector.Unprotect(stored.HealthNotes), protector.Unprotect(stored.CriminalRecordDate)));
        });
        plain.ShouldBe(("40", "AB Rh-", "Gluten alerjisi", "2026-01-10"));

        var audited = await factory.QueryAsync(db => db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == nameof(EmployeeSensitiveData) && e.EntityId == id.ToString() && e.Field != "Id")
            .Select(e => e.NewValue)
            .ToListAsync(Ct));
        audited.ShouldAllBe(value => value == AuditLogInterceptor.MaskedValue);
    }

    [Fact]
    public async Task Iban_NeedsTheSensitivePermission_AndIsValidated()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin);
        var iban = TurkishData.Iban();

        var invalid = await admin.PutAsJsonAsync($"{Url}/{id}/iban", new { version = await EmployeeSetup.VersionAsync(factory, id), iban = "TR00 1234" }, Ct);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["iban"]);

        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Update)
            .PutAsJsonAsync($"{Url}/{id}/iban", new { version = await EmployeeSetup.VersionAsync(factory, id), iban }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"{Url}/{id}/iban", new { version = await EmployeeSetup.VersionAsync(factory, id), iban }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await EmployeeSetup.LoadAsync(factory, id)).Iban.ShouldBe(iban.Replace(" ", string.Empty, StringComparison.Ordinal));
    }
}
