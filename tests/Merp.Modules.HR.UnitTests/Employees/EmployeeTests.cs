using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.Employees;

public class EmployeeTests
{
    private static readonly Guid Acme = Guid.CreateVersion7();
    private static readonly Guid Globex = Guid.CreateVersion7();
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly JobId Job = JobId.From(Guid.CreateVersion7());

    internal static PersonProfile Person() => new("Ayşe", "Yılmaz", new DateOnly(1990, 5, 1), "İstanbul", Gender.Female, "tr", MaritalStatus.Married, null);

    internal static EmploymentTerms Terms(Guid? company = null, DateOnly? hire = null, DateOnly? seniority = null, string? reason = null) =>
        new(company ?? Acme, hire ?? Jan1, seniority, reason, EmploymentType.Indefinite, null, null, null, null, null);

    internal static AssignmentDetails At(PositionId position, PositionId? manager = null) => new(position, null, manager, Job, "U2", "MM-100");

    internal static PositionId NewPosition() => PositionId.From(Guid.CreateVersion7());

    private static Employee Hire(PositionId? position = null, EmploymentTerms? terms = null) =>
        Employee.Hire(Tckn.Create("10000000146").Value, Person(), terms ?? Terms(), At(position ?? NewPosition()), "000001").Value;

    [Fact]
    public void Hire_CreatesThePrimaryEmployment_AndTheFirstAssignment()
    {
        var position = NewPosition();

        var employee = Hire(position);

        employee.Tckn.ShouldBe("10000000146");
        employee.FullName.ShouldBe("Ayşe Yılmaz");
        employee.Nationality.ShouldBe("TR");
        var employment = employee.Employments.ShouldHaveSingleItem();
        employment.CompanyId.ShouldBe(Acme);
        employment.RegistryNumber.ShouldBe("000001");
        employment.IsPrimary.ShouldBeTrue();
        employment.SeniorityDate.ShouldBe(Jan1);
        var assignment = employment.Assignments.ShouldHaveSingleItem();
        (assignment.PositionId, assignment.ValidFrom, assignment.ValidTo, assignment.Reason).ShouldBe((position, Jan1, (DateOnly?)null, AssignmentReason.Hire));
        employee.PrimaryEmploymentOn(Jan1).ShouldBe(employment);
    }

    [Fact]
    public void Hire_ChecksTheTerms()
    {
        Fields(Terms(seniority: Jan1.AddYears(-3))).ShouldBe(["seniorityReason"]);
        Fields(Terms(seniority: Jan1.AddDays(1))).ShouldBe(["seniorityDate"]);
        Fields(Terms() with { Type = EmploymentType.FixedTerm }).ShouldBe(["contractEndDate"]);
        Fields(Terms() with { Type = EmploymentType.PartTime, WeeklyHours = 50 }).ShouldBe(["weeklyHours"]);
        Fields(Terms() with { ProbationEndDate = Jan1.AddDays(-1) }).ShouldBe(["probationEndDate"]);
        Employee.Hire(Tckn.Create("10000000146").Value, Person(), Terms(seniority: Jan1.AddYears(-3), reason: "Önceki hizmet"), At(NewPosition()), "1")
            .Value.Employments[0].SeniorityDate.ShouldBe(Jan1.AddYears(-3));

        static List<string> Fields(EmploymentTerms terms) =>
            [.. Employee.Hire(Tckn.Create("10000000146").Value, Person(), terms, At(NewPosition()), "1").Error.Fields.Select(f => f.Field)];
    }

    [Fact]
    public void ChangeAssignment_InTheFuture_EndsThePreviousOneTheDayBefore()
    {
        var first = NewPosition();
        var second = NewPosition();
        var employee = Hire(first);
        var employment = employee.Employments[0];

        var changed = employee.ChangeAssignment(employment.Id, At(second), new DateOnly(2026, 3, 1), AssignmentReason.Promotion);

        changed.IsSuccess.ShouldBeTrue();
        employment.Assignments.Count.ShouldBe(2);
        employment.Assignments[0].ValidTo.ShouldBe(new DateOnly(2026, 2, 28));
        employment.AssignmentOn(new DateOnly(2026, 2, 28))!.PositionId.ShouldBe(first);
        employment.AssignmentOn(new DateOnly(2026, 3, 1))!.PositionId.ShouldBe(second);
        changed.Value.Reason.ShouldBe(AssignmentReason.Promotion);
        changed.Value.Announced.ShouldBeFalse();
    }

    [Fact]
    public void ChangeAssignment_OnTheLatestStart_CorrectsIt()
    {
        var corrected = NewPosition();
        var employee = Hire();
        var employment = employee.Employments[0];

        employee.ChangeAssignment(employment.Id, At(corrected), Jan1, AssignmentReason.Correction).IsSuccess.ShouldBeTrue();

        var assignment = employment.Assignments.ShouldHaveSingleItem();
        assignment.PositionId.ShouldBe(corrected);
        assignment.Reason.ShouldBe(AssignmentReason.Hire);
    }

    [Fact]
    public void ChangeAssignment_BeforeTheLatestOrTheHire_OrOnAClosedEmployment_IsRefused()
    {
        var employee = Hire();
        var employment = employee.Employments[0];
        employee.ChangeAssignment(employment.Id, At(NewPosition()), new DateOnly(2026, 3, 1), AssignmentReason.Transfer);

        employee.ChangeAssignment(employment.Id, At(NewPosition()), new DateOnly(2026, 2, 1), AssignmentReason.Transfer).Error.ShouldBe(EmployeeErrors.AssignmentBeforeLatest);
        employee.ChangeAssignment(employment.Id, At(NewPosition()), new DateOnly(2025, 12, 31), AssignmentReason.Transfer).Error.ShouldBe(EmployeeErrors.AssignmentBeforeHire);

        employee.Terminate(employment.Id, Termination(new DateOnly(2026, 4, 30)));
        employee.ChangeAssignment(employment.Id, At(NewPosition()), new DateOnly(2026, 6, 1), AssignmentReason.Transfer).Error.ShouldBe(EmployeeErrors.EmploymentClosed);
    }

    [Fact]
    public void Terminate_EndsTheCurrentAssignment_AndDropsLaterOnes()
    {
        var employee = Hire();
        var employment = employee.Employments[0];
        employee.ChangeAssignment(employment.Id, At(NewPosition()), new DateOnly(2026, 6, 1), AssignmentReason.Promotion);

        employee.Terminate(employment.Id, Termination(new DateOnly(2026, 3, 31))).IsSuccess.ShouldBeTrue();

        employment.IsOpen.ShouldBeFalse();
        employment.SgkExitCode.ShouldBe("03");
        employment.Assignments.ShouldHaveSingleItem().ValidTo.ShouldBe(new DateOnly(2026, 3, 31));
        employment.StatusOn(new DateOnly(2026, 3, 31)).ShouldBe(EmploymentStatus.Active);
        employment.StatusOn(new DateOnly(2026, 4, 1)).ShouldBe(EmploymentStatus.Terminated);
        employee.PrimaryEmploymentOn(new DateOnly(2026, 4, 1)).ShouldBeNull();
        employee.Terminate(employment.Id, Termination(new DateOnly(2026, 5, 1))).Error.ShouldBe(EmployeeErrors.EmploymentClosed);
    }

    [Fact]
    public void Terminate_BeforeTheHire_OrAnotherPersonsEmployment_IsRefused()
    {
        var employee = Hire(terms: Terms(hire: new DateOnly(2026, 2, 1)));

        employee.Terminate(employee.Employments[0].Id, Termination(Jan1)).Error.ShouldBe(EmployeeErrors.TerminationBeforeHire);
        employee.Terminate(Hire().Employments[0].Id, Termination(Jan1)).Error.ShouldBe(Merp.SharedKernel.Results.CommonErrors.NotFound);
    }

    [Fact]
    public void Terminate_PassesThePrimaryFlagToAnotherOpenEmployment()
    {
        var employee = Hire();
        var other = employee.Rehire(Terms(Globex, new DateOnly(2026, 2, 1)), At(NewPosition()), "000001").Value;
        other.IsPrimary.ShouldBeFalse();

        employee.Terminate(employee.Employments[0].Id, Termination(new DateOnly(2026, 3, 31)));

        other.IsPrimary.ShouldBeTrue();
        employee.Employments[0].IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public void Rehire_NeedsTheEmploymentInThatCompanyToHaveEnded()
    {
        var employee = Hire();
        var first = employee.Employments[0];

        employee.Rehire(Terms(hire: new DateOnly(2026, 6, 1)), At(NewPosition()), "2").Error.Fields.ShouldHaveSingleItem().Field.ShouldBe("companyId");

        employee.Terminate(first.Id, Termination(new DateOnly(2026, 3, 31)));
        employee.Rehire(Terms(hire: new DateOnly(2026, 3, 31)), At(NewPosition()), "2").Error.Fields.ShouldHaveSingleItem().Field.ShouldBe("hireDate");

        var rehired = employee.Rehire(Terms(hire: new DateOnly(2026, 6, 1)), At(NewPosition()), "000002");
        rehired.IsSuccess.ShouldBeTrue();
        rehired.Value.IsPrimary.ShouldBeTrue();
        rehired.Value.Assignments.ShouldHaveSingleItem().Reason.ShouldBe(AssignmentReason.Hire);
        employee.Employments.Count.ShouldBe(2);
    }

    [Fact]
    public void GroupTransfer_KeepsTheSeniority_AndMovesThePrimaryEmployment()
    {
        var employee = Hire(terms: Terms(seniority: new DateOnly(2020, 1, 1), reason: "Önceki hizmet"));
        var source = employee.Employments[0];
        var target = NewPosition();

        var moved = employee.GroupTransfer(source.Id, Terms(Globex, new DateOnly(2026, 7, 1)), "16", keepSeniority: true, At(target), "000001");

        moved.IsSuccess.ShouldBeTrue();
        source.TerminationDate.ShouldBe(new DateOnly(2026, 6, 30));
        source.SgkExitCode.ShouldBe("16");
        source.Rehireable.ShouldBe(true);
        var employment = moved.Value;
        employment.CompanyId.ShouldBe(Globex);
        employment.HireDate.ShouldBe(new DateOnly(2026, 7, 1));
        employment.SeniorityDate.ShouldBe(new DateOnly(2020, 1, 1));
        employment.IsPrimary.ShouldBeTrue();
        source.IsPrimary.ShouldBeFalse();
        employment.Assignments.ShouldHaveSingleItem().Reason.ShouldBe(AssignmentReason.Transfer);
        employee.PrimaryEmploymentOn(new DateOnly(2026, 7, 1)).ShouldBe(employment);
    }

    [Fact]
    public void GroupTransfer_WithoutKeepingSeniority_StartsItAgain()
    {
        var employee = Hire(terms: Terms(seniority: new DateOnly(2020, 1, 1), reason: "Önceki hizmet"));

        var moved = employee.GroupTransfer(employee.Employments[0].Id, Terms(Globex, new DateOnly(2026, 7, 1)), "16", keepSeniority: false, At(NewPosition()), "1");

        moved.Value.SeniorityDate.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public void GroupTransfer_ToTheSameCompany_OrBeforeTheHire_IsRefused()
    {
        var employee = Hire();
        var source = employee.Employments[0].Id;

        employee.GroupTransfer(source, Terms(Acme, new DateOnly(2026, 7, 1)), "16", true, At(NewPosition()), "1").Error.ShouldBe(EmployeeErrors.TransferToSameCompany);
        employee.GroupTransfer(source, Terms(Globex, Jan1), "16", true, At(NewPosition()), "1").Error.ShouldBe(EmployeeErrors.TransferBeforeHire);
        employee.Employments.ShouldHaveSingleItem().IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void StatusOn_FollowsTheDates()
    {
        var employee = Hire(terms: Terms(hire: new DateOnly(2026, 2, 1)));
        var employment = employee.Employments[0];

        employment.StatusOn(Jan1).ShouldBe(EmploymentStatus.Upcoming);
        employment.StatusOn(new DateOnly(2026, 2, 1)).ShouldBe(EmploymentStatus.Active);
        employee.PrimaryEmploymentOn(Jan1).ShouldBeNull();
    }

    [Fact]
    public void SetContacts_RefusesTwoPrimaryAddresses()
    {
        var employee = Hire();
        EmployeeEmail[] emails = [new(ContactKind.Work, "a@acme.com.tr", true), new(ContactKind.Personal, "a@example.com", true)];

        Should.Throw<ArgumentException>(() => employee.SetContacts(emails, [], [], []));
    }

    [Fact]
    public void AssignmentToItsOwnPosition_AsManager_Throws()
    {
        var position = NewPosition();

        Should.Throw<ArgumentException>(() => Employee.Hire(Tckn.Create("10000000146").Value, Person(), Terms(), At(position, position), "1"));
    }

    private static TerminationDetails Termination(DateOnly date) => new(date, "03", "İstifa", true, null);
}
