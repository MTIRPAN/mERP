using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.PreHires;
using Merp.Modules.HR.UnitTests.Employees;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.PreHires;

/// <summary>Pre-hires of candidates Recruitment hires (PLAN R7, D54).</summary>
public sealed class PreHireTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private static PreHire Open() => PreHire.Open(new PreHireDetails(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        null,
        null,
        "Satış Uzmanı",
        new DateOnly(2026, 12, 1),
        EmploymentType.Indefinite,
        "Ayşe",
        "Yılmaz",
        "ayse@ornek.test",
        null,
        null));

    [Fact]
    public void APreHire_WaitsForHr_AndIsCompletedOnce()
    {
        var preHire = Open();
        preHire.ShouldSatisfyAllConditions(p => p.Status.ShouldBe(PreHireStatus.Pending), p => p.FullName.ShouldBe("Ayşe Yılmaz"));

        var employee = EmployeeId.From(Guid.CreateVersion7());
        var employment = EmploymentId.From(Guid.CreateVersion7());
        preHire.Complete(employee, employment, At).ShouldBeTrue();
        preHire.ShouldSatisfyAllConditions(
            p => p.Status.ShouldBe(PreHireStatus.Completed),
            p => p.EmployeeId.ShouldBe(employee),
            p => p.EmploymentId.ShouldBe(employment),
            p => p.CompletedAt.ShouldBe(At));

        preHire.Complete(EmployeeId.From(Guid.CreateVersion7()), employment, At).ShouldBeFalse();
        preHire.Cancel(null).Error.ShouldBe(PreHireErrors.NotPending);
    }

    [Fact]
    public void ACancelledPreHire_IsNotCompleted()
    {
        var preHire = Open();

        preHire.Cancel("Aday işe başlamadı").IsSuccess.ShouldBeTrue();

        preHire.ShouldSatisfyAllConditions(p => p.Status.ShouldBe(PreHireStatus.Cancelled), p => p.CancelReason.ShouldBe("Aday işe başlamadı"));
        preHire.Complete(EmployeeId.From(Guid.CreateVersion7()), EmploymentId.From(Guid.CreateVersion7()), At).ShouldBeFalse();
    }

    [Fact]
    public void TheEmployee_ConfirmsThePreHire_WithADomainEvent()
    {
        var employee = Employee.Hire(Tckn.Create("10000000146").Value, EmployeeTests.Person(), EmployeeTests.Terms(), EmployeeTests.At(EmployeeTests.NewPosition()), "000001").Value;
        var preHire = Open();
        var employment = employee.Employments[0];

        employee.ConfirmPreHire(preHire.Id, employment.Id, employment.HireDate);

        var confirmed = employee.DomainEvents.OfType<PreHireConfirmed>().ShouldHaveSingleItem();
        confirmed.ShouldBe(new PreHireConfirmed(preHire.Id, employee.Id, employment.Id, employment.HireDate));
    }
}
