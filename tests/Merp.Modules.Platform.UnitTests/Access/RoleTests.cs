using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Roles;
using Merp.SharedKernel.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace Merp.Modules.Platform.UnitTests.Access;

public class RoleTests
{
    private static Role NewRole(params string[] permissions) =>
        Role.Create(Code.Create("payroll", Role.CodeMaxLength).Value, LocalizedString.Of("tr-TR", "Bordro"), null, permissions);

    [Fact]
    public void SetPermissions_ReplacesTheSet_AndReportsChanges()
    {
        var role = NewRole("platform.companies.read", "platform.companies.read", " ");

        role.Code.ShouldBe("PAYROLL");
        role.Permissions.Select(p => p.PermissionCode).ShouldBe(["platform.companies.read"]);

        role.SetPermissions(["platform.companies.read"]).ShouldBeFalse();
        role.SetPermissions(["platform.branches.read"]).ShouldBeTrue();
        role.Has("platform.branches.read").ShouldBeTrue();
        role.Has("platform.companies.read").ShouldBeFalse();
    }

    [Fact]
    public void Grant_AddsWithoutRemoving()
    {
        var role = NewRole("platform.companies.read");

        role.Grant(["platform.users.read"]);

        role.Permissions.Select(p => p.PermissionCode).ShouldBe(["platform.companies.read", "platform.users.read"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("platform.users.manage", true)]
    [InlineData("platform.anything.new", true)]
    [InlineData("hr.employees.read", false)]
    [InlineData("platformx.users.read", false)]
    public void SystemAdministrator_GetsEveryPlatformPermission(string permission, bool granted) =>
        SystemRoles.SystemAdministrator.Grants(permission).ShouldBe(granted);

    [Theory]
    [InlineData("hr.employees.read", true)]
    [InlineData("hr.employees.terminate", true)]
    [InlineData("hr.employees.sensitive.read", false)]
    [InlineData("hr.leave.requests.approve", true)]
    [InlineData("hr.org.read", true)]
    [InlineData("hr.org.manage", false)]
    public void HrSpecialist_ExcludesSensitiveData(string permission, bool granted) =>
        SystemRoles.HrSpecialist.Grants(permission).ShouldBe(granted);

    [Fact]
    public void DefaultPermissions_PicksFromTheCatalog() =>
        SystemRoles.Employee.DefaultPermissions(["hr.self.read", "hr.self.leave.request", "hr.employees.read"])
            .ShouldBe(["hr.self.read", "hr.self.leave.request"]);

    [Fact]
    public void SystemRoleCodes_AreUnique_AndFoundIgnoringCase()
    {
        SystemRoles.All.Select(r => r.Code).ShouldBeUnique();
        SystemRoles.Find("SYSTEM-ADMIN").ShouldBe(SystemRoles.SystemAdministrator);
        SystemRoles.Find("unknown").ShouldBeNull();
    }

    [Theory]
    [InlineData("2026-10-05T20:59:59Z", "2026-10-05")]
    [InlineData("2026-10-05T21:00:00Z", "2026-10-06")]
    public void PlatformClock_CountsDaysInIstanbul(string utc, string day)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        PlatformClock.Today(time).ShouldBe(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture));
        PlatformClock.EndOfToday(time).ShouldBeGreaterThan(time.GetUtcNow());
        (PlatformClock.EndOfToday(time) - time.GetUtcNow()).ShouldBeLessThanOrEqualTo(TimeSpan.FromDays(1));
    }
}
