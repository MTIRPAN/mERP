using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Infrastructure.Leave;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>Started versions are history; versions to come may change (D41).</summary>
public sealed class LeaveVersionsTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly AnnualLeaveRules Legal = LeaveSeeder.LegalRules();

    private static LeavePolicy Policy(params LeavePolicyVersion[] versions) =>
        LeavePolicy.Create(Code.Create("TEST").Value, LocalizedString.Of("tr-TR", "Test"), null, false, versions).Value;

    [Fact]
    public void AFutureVersion_IsAdded_ChangedAndRemoved()
    {
        var policy = Policy(new LeavePolicyVersion(DateOnly.MinValue, Legal));
        var future = new LeavePolicyVersion(new DateOnly(2027, 1, 1), Legal with { AdvanceLimitDays = 5 });

        policy.Update(policy.Name, false, true, [.. policy.Versions, future], Today).IsSuccess.ShouldBeTrue();
        policy.VersionOn(new DateOnly(2027, 2, 1))!.Rules.AdvanceLimitDays.ShouldBe(5);
        policy.VersionOn(Today)!.Rules.AdvanceLimitDays.ShouldBe(0);

        policy.Update(policy.Name, false, true, [policy.Versions[0], future with { Rules = Legal with { AdvanceLimitDays = 3 } }], Today).IsSuccess.ShouldBeTrue();
        policy.Update(policy.Name, false, true, [policy.Versions[0]], Today).IsSuccess.ShouldBeTrue();
        policy.Versions.ShouldHaveSingleItem();
    }

    [Fact]
    public void AStartedVersion_CannotBeChangedOrRemoved_NorANewOneStartToday()
    {
        var policy = Policy(new LeavePolicyVersion(DateOnly.MinValue, Legal), new LeavePolicyVersion(new DateOnly(2026, 1, 1), Legal with { AdvanceLimitDays = 2 }));

        Error(policy, [policy.Versions[0], policy.Versions[1] with { Rules = Legal }]).ShouldBe(Versions.LockedCode);
        Error(policy, [policy.Versions[0]]).ShouldBe(Versions.LockedCode);
        Error(policy, [.. policy.Versions, new LeavePolicyVersion(Today, Legal)]).ShouldBe(Versions.LockedCode);
        Error(policy, [.. policy.Versions, new LeavePolicyVersion(new DateOnly(2027, 1, 1), Legal), new LeavePolicyVersion(new DateOnly(2027, 1, 1), Legal)])
            .ShouldBe(Versions.DuplicateCode);
        Error(policy, []).ShouldBe(Versions.RequiredCode);
    }

    [Fact]
    public void AStartedVersion_ComparesByValue_ListsIncluded()
    {
        var policy = Policy(new LeavePolicyVersion(DateOnly.MinValue, Legal));
        var copy = new LeavePolicyVersion(DateOnly.MinValue, Legal with { Tiers = [.. Legal.Tiers], ExtraDayRules = [.. Legal.ExtraDayRules] });

        policy.Update(policy.Name, false, true, [copy], Today).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ADefaultPolicy_CannotBeDeactivated_AndBecomingTheDefaultIsAnnounced()
    {
        var policy = Policy(new LeavePolicyVersion(DateOnly.MinValue, Legal));

        policy.Update(policy.Name, isDefault: true, isActive: false, policy.Versions, Today).Error.Fields.ShouldHaveSingleItem().Code
            .ShouldBe(LeavePolicyErrors.DefaultInactiveCode);
        policy.Update(policy.Name, isDefault: true, isActive: true, policy.Versions, Today).IsSuccess.ShouldBeTrue();

        policy.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LeavePolicyMadeDefault>();
    }

    private static string Error(LeavePolicy policy, LeavePolicyVersion[] versions) =>
        policy.Update(policy.Name, false, true, versions, Today).Error.Fields.ShouldHaveSingleItem().Code;
}
