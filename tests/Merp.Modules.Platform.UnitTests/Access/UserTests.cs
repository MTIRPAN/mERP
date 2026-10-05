using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.UnitTests.Access;

public class UserTests
{
    private static readonly RoleId Admin = RoleId.From(Guid.CreateVersion7());
    private static readonly RoleId Reader = RoleId.From(Guid.CreateVersion7());
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static User NewUser() => User.Create("6f1c-sub", new UserProfile("ayse", "Ayse@Example.com", "Ayşe", "Yılmaz"));

    [Fact]
    public void Create_IsActive_WithNormalizedProfile()
    {
        var user = NewUser();

        user.IsActive.ShouldBeTrue();
        user.Subject.ShouldBe("6f1c-sub");
        user.Email.ShouldBe("ayse@example.com");
        user.DisplayName.ShouldBe("Ayşe Yılmaz");
        user.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateProfile_ReportsWhetherSomethingChanged()
    {
        var user = NewUser();

        user.UpdateProfile(new UserProfile("ayse", "ayse@example.com", "Ayşe", "Yılmaz")).ShouldBeFalse();
        user.UpdateProfile(new UserProfile("ayse", "ayse@example.com", "Ayşe", "Kaya")).ShouldBeTrue();
        user.DisplayName.ShouldBe("Ayşe Kaya");
    }

    [Fact]
    public void DisplayName_FallsBackToTheUserName() =>
        User.Create("sub", new UserProfile("servis", null, null, null)).DisplayName.ShouldBe("servis");

    [Fact]
    public void ValidAssignments_KeepOnlyThoseInForceOnTheDay()
    {
        var user = NewUser();
        var open = user.Assign(Admin, new AssignmentScope("Tenant"), null, null).Value;
        var current = user.Assign(Reader, new AssignmentScope("Company", Guid.CreateVersion7()), Today.AddDays(-10), Today).Value;
        user.Assign(Reader, new AssignmentScope("Company", Guid.CreateVersion7()), Today.AddDays(-10), Today.AddDays(-1));
        user.Assign(Reader, new AssignmentScope("Branch", Guid.CreateVersion7()), Today.AddDays(1), null);

        user.ValidAssignments(Today).ShouldBe([open, current], ignoreOrder: true);
    }

    [Fact]
    public void InactiveUser_HasNoValidAssignments()
    {
        var user = NewUser();
        user.Assign(Admin, new AssignmentScope("Tenant"), null, null);

        user.Deactivate();

        user.ValidAssignments(Today).ShouldBeEmpty();
        user.Activate();
        user.ValidAssignments(Today).ShouldHaveSingleItem();
    }

    [Fact]
    public void Assign_RejectsAnEndBeforeTheStart()
    {
        var error = NewUser().Assign(Admin, new AssignmentScope("Tenant"), Today, Today.AddDays(-1)).Error;

        error.Fields.ShouldHaveSingleItem().Field.ShouldBe("validTo");
        error.Fields[0].Code.ShouldBe(CommonErrors.InvalidDateRange.Code);
    }

    [Fact]
    public void Assign_RejectsAnIdenticalAssignment_ButAcceptsAnotherScope()
    {
        var user = NewUser();
        var company = Guid.CreateVersion7();
        user.Assign(Reader, new AssignmentScope("Company", company), null, null);

        user.Assign(Reader, new AssignmentScope("Company", company), null, null).Error.ShouldBe(UserErrors.DuplicateAssignment);
        user.Assign(Reader, new AssignmentScope("Company", Guid.CreateVersion7()), null, null).IsSuccess.ShouldBeTrue();
        user.Assign(Reader, new AssignmentScope("Company", company), Today, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ChangeValidity_AndUnassign_FindTheAssignment()
    {
        var user = NewUser();
        var assignment = user.Assign(Admin, new AssignmentScope("Tenant"), null, null).Value;

        user.ChangeValidity(assignment.Id, null, Today.AddDays(-1)).IsSuccess.ShouldBeTrue();
        user.ValidAssignments(Today).ShouldBeEmpty();
        user.ChangeValidity(assignment.Id, Today, Today.AddDays(-1)).IsFailure.ShouldBeTrue();
        user.ChangeValidity(RoleAssignmentId.From(Guid.CreateVersion7()), null, null).Error.ShouldBe(CommonErrors.NotFound);

        user.Unassign(assignment.Id).IsSuccess.ShouldBeTrue();
        user.Assignments.ShouldBeEmpty();
        user.Unassign(assignment.Id).Error.ShouldBe(CommonErrors.NotFound);
    }

    [Fact]
    public void SetCulture_BlankFollowsTheBrowser()
    {
        var user = NewUser();

        user.SetCulture(" en-US ");
        user.Culture.ShouldBe("en-US");
        user.SetCulture(" ");
        user.Culture.ShouldBeNull();
    }
}
