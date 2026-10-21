using Merp.Modules.HR.Domain.Employees;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.Employees;

/// <summary>The person's side of the employee–user link (D39); Platform writes the link and reports back.</summary>
public class EmployeeUserLinkTests
{
    private static Employee Hire() =>
        Employee.Hire(
            Tckn.Create("10000000146").Value,
            EmployeeTests.Person(),
            EmployeeTests.Terms(),
            EmployeeTests.At(EmployeeTests.NewPosition()),
            "000001").Value;

    [Fact]
    public void RequestUserLink_WaitsForPlatform_OneRequestAtATime()
    {
        var employee = Hire();

        employee.RequestUserLink().IsSuccess.ShouldBeTrue();

        employee.UserLinkState.ShouldBe(UserLinkState.Pending);
        employee.RequestUserLink().Error.ShouldBe(EmployeeErrors.UserLinkPending);
        employee.RequestUserUnlink().Error.ShouldBe(EmployeeErrors.UserLinkPending);
    }

    [Fact]
    public void UserLinked_KeepsTheUser_AndALinkedPersonCannotAskForAnother()
    {
        var employee = Hire();
        var userId = Guid.NewGuid();
        employee.RequestUserLink();

        employee.UserLinked(userId);

        employee.UserId.ShouldBe(userId);
        employee.UserLinkState.ShouldBe(UserLinkState.Linked);
        employee.RequestUserLink().Error.ShouldBe(EmployeeErrors.UserAlreadyLinked);
    }

    [Fact]
    public void UserLinkFailed_KeepsTheReason_AndAllowsANewRequest()
    {
        var employee = Hire();
        employee.RequestUserLink();

        employee.UserLinkFailed("platform.users.errors.email_taken");

        employee.UserLinkState.ShouldBe(UserLinkState.Failed);
        employee.UserLinkError.ShouldBe("platform.users.errors.email_taken");
        employee.RequestUserLink().IsSuccess.ShouldBeTrue();
        employee.UserLinkError.ShouldBeNull();
    }

    [Fact]
    public void UserUnlinked_ClearsTheLink_ButNotALinkToAnotherUser()
    {
        var employee = Hire();
        var userId = Guid.NewGuid();
        employee.UserLinked(userId);
        employee.RequestUserUnlink().IsSuccess.ShouldBeTrue();

        employee.UserUnlinked(Guid.NewGuid());
        employee.UserId.ShouldBe(userId);
        employee.UserLinkState.ShouldBe(UserLinkState.Linked);

        employee.UserUnlinked(userId);
        employee.UserId.ShouldBeNull();
        employee.UserLinkState.ShouldBe(UserLinkState.None);
        employee.RequestUserUnlink().Error.ShouldBe(EmployeeErrors.NoUser);
    }

    [Fact]
    public void Rehire_AsksForTheDeactivationAgainAtTheNextLeave()
    {
        var employee = Hire();
        employee.UserLinked(Guid.NewGuid());
        var employment = employee.Employments[0];
        employee.Terminate(employment.Id, new TerminationDetails(new DateOnly(2026, 3, 31), "03", null, true, null)).IsSuccess.ShouldBeTrue();
        employee.MarkUserDeactivationRequested(DateTimeOffset.UnixEpoch);

        employee.Rehire(EmployeeTests.Terms(hire: new DateOnly(2026, 6, 1)), EmployeeTests.At(EmployeeTests.NewPosition()), "000002").IsSuccess.ShouldBeTrue();

        employee.UserDeactivationRequestedAt.ShouldBeNull();
    }
}
