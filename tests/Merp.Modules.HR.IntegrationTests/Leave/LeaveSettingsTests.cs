using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Infrastructure.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Testing.Web;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>Leave types, policies, their assignment rules and which policy applies (PLAN L1, D41).</summary>
public sealed class LeaveSettingsTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private static readonly AnnualLeaveRules Legal = LeaveSeeder.LegalRules();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> Name(string text) => new() { ["tr-TR"] = text };

    [Fact]
    public async Task Seed_HoldsTheLeaveTypesOfTheLaw_AndTheLegalPolicyAsTheDefault()
    {
        var admin = factory.Admin();

        var types = (await admin.GetFromJsonAsync<List<LeaveTypeItem>>("/api/hr/leave/types", Ct))!;
        var policies = (await admin.GetFromJsonAsync<List<LeavePolicyItem>>("/api/hr/leave/policies", Ct))!;

        types.Select(t => t.Code).ShouldContain("YILLIK");
        types.Single(t => t.Code == "BABALIK").Versions.Select(v => v.FixedDays).ShouldBe([5m, 10m]);
        types.Single(t => t.Code == "EVLILIK").Name.ShouldBe("Evlilik izni");
        types.ShouldAllBe(t => t.Warnings.Count == 0);
        var legal = policies.Single(p => p.Code == LeaveSeeder.LegalPolicyCode);
        legal.IsDefault.ShouldBeTrue();
        legal.Warnings.ShouldBeEmpty();
        legal.Versions.ShouldHaveSingleItem().Rules.Tiers.Select(t => t.Days).ShouldBe([14m, 20m, 26m]);
    }

    [Fact]
    public async Task LeaveType_IsCreated_AndOnlyItsFutureVersionsChange()
    {
        var admin = factory.Admin();
        var code = HrFactory.NewCode("LT");
        var body = new
        {
            code,
            name = Name("Kan bağışı izni"),
            category = "Administrative",
            payer = "Employer",
            unit = "Day",
            durationBasis = "WorkingDays",
            versions = new[] { new LeaveTypeVersion(DateOnly.MinValue, FixedDays: 1) },
        };

        var created = await admin.PostAsJsonAsync("/api/hr/leave/types", body, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync("/api/hr/leave/types", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/hr/leave/types", body with { code = HrFactory.NewCode("LT"), versions = Array.Empty<LeaveTypeVersion>() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var type = await TypeAsync(admin, code);
        var future = new LeaveTypeVersion(new DateOnly(2099, 1, 1), FixedDays: 2);
        var update = new
        {
            version = type.Version,
            name = Name("Kan bağışı izni"),
            category = "Administrative",
            payer = "Employer",
            unit = "Day",
            durationBasis = "WorkingDays",
            isActive = true,
            versions = new[] { type.Versions[0], future },
        };
        (await admin.PutAsJsonAsync($"/api/hr/leave/types/{type.Id}", update, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        type = await TypeAsync(admin, code);
        type.Versions.Select(v => v.FixedDays).ShouldBe([1m, 2m]);

        var locked = await admin.PutAsJsonAsync(
            $"/api/hr/leave/types/{type.Id}",
            update with { version = type.Version, versions = [type.Versions[0] with { FixedDays = 5 }, future] },
            Ct);
        locked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(locked)).Errors!["versions"].ShouldContain(m => m.Contains("Başlamış bir sürüm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordsOfEveryCompany_NeedTheTenantScope_AndReadersCannotChangeAnything()
    {
        var companyId = await HrFactory.CreateCompanyAsync(factory.Admin());
        var companyScoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Settings.Read, HrPermissions.Leave.Settings.Manage)
            .WithScopes(AccessScope.Company(companyId));
        var policy = new { code = HrFactory.NewCode("LP"), name = Name("Şirket politikası"), versions = new[] { new LeavePolicyVersion(DateOnly.MinValue, Legal) } };

        (await companyScoped.PostAsJsonAsync("/api/hr/leave/policies", policy, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await companyScoped.PostAsJsonAsync("/api/hr/leave/policies", new { policy.code, policy.name, policy.versions, companyId }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var reader = factory.CreateClientFor("okur", HrFactory.Tenant, HrPermissions.Leave.Settings.Read);
        (await reader.PostAsJsonAsync("/api/hr/leave/policies", policy, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var stranger = factory.CreateClientFor("yabanci", HrFactory.Tenant, HrPermissions.Employees.Read);
        (await stranger.GetAsync("/api/hr/leave/types", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PolicyBelowTheLaw_IsSavedWithWarnings_AndTheFormCanCheckItFirst()
    {
        // PLAN L1 acceptance: a setting below the legal minimum gives a warning
        var admin = factory.Admin();
        var low = Legal with { Tiers = [new EntitlementTier(1, 12), new EntitlementTier(5, 20), new EntitlementTier(15, 26)] };

        var check = await admin.PostAsJsonAsync("/api/hr/leave/policies/legal-check", new { rules = low }, Ct);
        check.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await check.Content.ReadFromJsonAsync<List<LeaveLegalWarning>>(Ct))!.ShouldHaveSingleItem().Message
            .ShouldBe("1 yıllık kıdemde 12 gün veriliyor; kanuni alt sınır 14 gün.");

        var code = HrFactory.NewCode("LP");
        (await admin.PostAsJsonAsync("/api/hr/leave/policies", new { code, name = Name("Düşük"), versions = new[] { new LeavePolicyVersion(DateOnly.MinValue, low) } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PolicyAsync(admin, code)).Warnings.ShouldHaveSingleItem().Code.ShouldBe("hr.leave.legal.tier");

        var invalid = Legal with { Tiers = [] };
        (await admin.PostAsJsonAsync("/api/hr/leave/policies/legal-check", new { rules = invalid }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ANewDefaultPolicy_TakesOverFromThePreviousOne()
    {
        var admin = factory.Admin();
        var companyId = await HrFactory.CreateCompanyAsync(admin);
        var first = HrFactory.NewCode("LP");
        var second = HrFactory.NewCode("LP");
        object Body(string code) => new { code, name = Name(code), companyId, isDefault = true, versions = new[] { new LeavePolicyVersion(DateOnly.MinValue, Legal) } };

        (await admin.PostAsJsonAsync("/api/hr/leave/policies", Body(first), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync("/api/hr/leave/policies", Body(second), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PolicyAsync(admin, first)).IsDefault.ShouldBeFalse();
        (await PolicyAsync(admin, second)).IsDefault.ShouldBeTrue();
        (await PolicyAsync(admin, LeaveSeeder.LegalPolicyCode)).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Resolution_FollowsTheOverride_ThenTheRules_ThenTheDefault()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employeeId = await setup.HireAsync(admin, new DateOnly(2026, 1, 5));
        var employmentId = (await EmployeeSetup.LoadAsync(factory, employeeId)).Employments.Single().Id.Value;
        var generous = HrFactory.NewCode("LP");
        await admin.PostAsJsonAsync(
            "/api/hr/leave/policies",
            new { code = generous, name = Name("Cömert"), companyId = setup.CompanyId, versions = new[] { new LeavePolicyVersion(DateOnly.MinValue, Legal with { Tiers = [new EntitlementTier(1, 18)] }) } },
            Ct);
        var generousId = (await PolicyAsync(admin, generous)).Id;
        var legalId = (await PolicyAsync(admin, LeaveSeeder.LegalPolicyCode)).Id;
        var url = $"/api/hr/leave/policy-resolution?employmentId={employmentId}&date=2030-01-05";

        var byDefault = (await admin.GetFromJsonAsync<LeavePolicyResolution>(url, Ct))!;
        byDefault.Source.ShouldBe(LeavePolicySource.TenantDefault);
        byDefault.PolicyId.ShouldBe(legalId);
        byDefault.CompletedYears.ShouldBe(4);
        byDefault.EntitlementDays.ShouldBe(14);

        var rules = new
        {
            version = (uint?)null,
            rules = new[]
            {
                new { name = "Stajyerler", policyId = legalId, criteria = new[] { new PolicyCriterion(CriterionKind.EmploymentType, ["Intern"]) } },
                new { name = "Satış birimi", policyId = generousId, criteria = new[] { new PolicyCriterion(CriterionKind.OrgUnitTree, [setup.UnitId.ToString()]) } },
            },
        };
        (await admin.PutAsJsonAsync($"/api/hr/leave/policy-rules/{setup.CompanyId}", rules, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var byRule = (await admin.GetFromJsonAsync<LeavePolicyResolution>(url, Ct))!;
        byRule.Source.ShouldBe(LeavePolicySource.Rule);
        byRule.RulePosition.ShouldBe(2);
        byRule.RuleName.ShouldBe("Satış birimi");
        byRule.EntitlementDays.ShouldBe(18);

        var overrideUrl = $"/api/hr/employees/{employeeId}/employments/{employmentId}/leave-policy";
        (await admin.PutAsJsonAsync(overrideUrl, new { version = await EmployeeSetup.VersionAsync(factory, employeeId), policyId = legalId }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<LeavePolicyResolution>(url, Ct))!.Source.ShouldBe(LeavePolicySource.Override);

        (await admin.PutAsJsonAsync(overrideUrl, new { version = await EmployeeSetup.VersionAsync(factory, employeeId), policyId = (Guid?)null }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<LeavePolicyResolution>(url, Ct))!.Source.ShouldBe(LeavePolicySource.Rule);
        (await admin.GetFromJsonAsync<LeavePolicyRules>($"/api/hr/leave/policy-rules/{setup.CompanyId}", Ct))!.Rules.Select(r => r.PolicyName)
            .ShouldBe(["Kanuni yıllık izin (4857)", "Cömert"]);
    }

    [Fact]
    public async Task Rules_NameOnlyUsablePolicies_AndUnitsOfTheirCompany()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var foreign = HrFactory.NewCode("LP");
        await admin.PostAsJsonAsync(
            "/api/hr/leave/policies",
            new { code = foreign, name = Name("Başka şirket"), companyId = other.CompanyId, versions = new[] { new LeavePolicyVersion(DateOnly.MinValue, Legal) } },
            Ct);
        var legalId = (await PolicyAsync(admin, LeaveSeeder.LegalPolicyCode)).Id;
        object Rules(Guid policyId, string unit) => new
        {
            rules = new[] { new { name = "Kural", policyId, criteria = new[] { new PolicyCriterion(CriterionKind.OrgUnit, [unit]) } } },
        };

        var response = await admin.PutAsJsonAsync($"/api/hr/leave/policy-rules/{setup.CompanyId}", Rules((await PolicyAsync(admin, foreign)).Id, other.UnitId.ToString()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Problem.ReadAsync(response)).Errors!;
        errors.ShouldContainKey("rules[0].policyId");
        errors.ShouldContainKey("rules[0].criteria[0].values");
        (await admin.PutAsJsonAsync($"/api/hr/leave/policy-rules/{setup.CompanyId}", Rules(legalId, setup.UnitId.ToString()), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OutsideTheScope_RulesAndResolutionsAreNotFound()
    {
        var admin = factory.Admin();
        var own = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var employeeId = await other.HireAsync(admin);
        var employmentId = (await EmployeeSetup.LoadAsync(factory, employeeId)).Employments.Single().Id.Value;
        var reader = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Settings.Read, HrPermissions.Leave.Settings.Manage)
            .WithScopes(AccessScope.Company(own.CompanyId));

        (await reader.GetAsync($"/api/hr/leave/policy-rules/{other.CompanyId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/hr/leave/policy-resolution?employmentId={employmentId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.PutAsJsonAsync($"/api/hr/leave/policy-rules/{other.CompanyId}", new { rules = Array.Empty<object>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PutAsJsonAsync($"/api/hr/employees/{employeeId}/employments/{employmentId}/leave-policy", new { version = 0, policyId = (Guid?)null }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/hr/leave/policy-rules/{own.CompanyId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<LeaveTypeItem> TypeAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<LeaveTypeItem>>("/api/hr/leave/types?includeInactive=true", Ct))!.Single(t => t.Code == code);

    private static async Task<LeavePolicyItem> PolicyAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<LeavePolicyItem>>("/api/hr/leave/policies?includeInactive=true", Ct))!.Single(p => p.Code == code);
}
