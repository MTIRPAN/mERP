using Merp.ArchitectureTests.Rules;
using Merp.ArchitectureTests.Samples.BadContracts;

namespace Merp.ArchitectureTests;

/// <summary>Each rule must turn red on a deliberate violation; otherwise a green architecture run proves nothing.</summary>
public class RuleSelfTests
{
    private static readonly System.Reflection.Assembly Samples = typeof(RuleSelfTests).Assembly;

    [Fact]
    public void ForbiddenDependencies_FlagsDomainTypeUsingEfCore() =>
        DependencyRules.ForbiddenDependencies(Samples, DependencyRules.ForbiddenInDomain, "Merp.ArchitectureTests.Samples.BadDomain")
            .ShouldContain(name => name.EndsWith("EntityWithDbContext", StringComparison.Ordinal));

    [Fact]
    public void CrossModuleReferences_FlagsReferenceToAnotherModulesApplication()
    {
        var violations = DependencyRules.CrossModuleReferences(
        [
            ("Merp.Modules.HR.Application", ["Merp.Modules.Platform.Contracts", "Merp.Modules.Platform.Application"]),
            ("Merp.Modules.HR.Infrastructure", ["Merp.Modules.HR.Application", "Merp.Infrastructure"]),
        ]);

        violations.ShouldBe(["Merp.Modules.HR.Application -> Merp.Modules.Platform.Application"]);
    }

    [Fact]
    public void LayerReferences_FlagsDomainReferencingApplication() =>
        DependencyRules.LayerReferences([("Merp.Modules.HR.Domain", ["Merp.Modules.HR.Application"])])
            .ShouldBe(["Merp.Modules.HR.Domain -> HR.Application"]);

    [Fact]
    public void ContractsRule_FlagsWritesAndSynchronousMethods()
    {
        var violations = ContractsRules.NonQueryInterfaceMethods([typeof(IEmployeeCommands)]);

        violations.ShouldContain(v => v.EndsWith(".CreateEmployeeAsync", StringComparison.Ordinal));
        violations.ShouldContain(v => v.EndsWith(".GetName", StringComparison.Ordinal));
        violations.ShouldNotContain(v => v.EndsWith(".FindNameAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void NamingRule_FlagsTypesOfTwoModulesWithTheSameName() =>
        NamingRules.SharedNames(
        [
            ("HR", typeof(Samples.BadNames.Hr.AddressInput)),
            ("Platform", typeof(Samples.BadNames.Platform.AddressInput)),
            ("Platform", typeof(Samples.BadNames.Platform.IAddressFields)),
            ("HR", typeof(Samples.BadNames.Hr.IAddressFields)),
        ]).ShouldBe(["AddressInput: Merp.ArchitectureTests.Samples.BadNames.Hr.AddressInput, Merp.ArchitectureTests.Samples.BadNames.Platform.AddressInput"]);

    [Fact]
    public void ClockRule_FlagsSystemClockReads() =>
        ClockRules.SystemClockUsages(Samples, "Merp.ArchitectureTests.Samples.BadClock").Count.ShouldBe(2);

    [Fact]
    public void EndpointRule_FlagsUnlistedAnonymousAndPermissionlessEndpoints()
    {
        var violations = EndpointRules.Violations(
        [
            new("/api/hr/employees", AllowsAnonymous: true, RequiresAuthorization: false, []),
            new("/api/hr/leave", AllowsAnonymous: false, RequiresAuthorization: true, []),
            new("/api/hr/org", AllowsAnonymous: false, RequiresAuthorization: false, []),
            new("/api/platform/companies", AllowsAnonymous: false, RequiresAuthorization: true, ["platform.companies.read"]),
            new("/api/public/{tenantCode}/careers/jobs", AllowsAnonymous: true, RequiresAuthorization: false, []),
            new("/health/ready", AllowsAnonymous: true, RequiresAuthorization: false, []),
            new("/api/platform/me", AllowsAnonymous: false, RequiresAuthorization: true, [], AuthenticatedUserOnly: true),
            new("/api/hr/everything", AllowsAnonymous: false, RequiresAuthorization: true, [], AuthenticatedUserOnly: true),
        ]);

        violations.Count.ShouldBe(4);
        violations.ShouldAllBe(v => v.StartsWith("/api/hr/", StringComparison.Ordinal));
    }
}
