using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Domain.Employees;

namespace Merp.Modules.HR.UnitTests.Employees;

public class EmployeeValidatorTests
{
    [Fact]
    public void Hire_ReportsEachInvalidField()
    {
        var result = new HireEmployeeCommandValidator().Validate(new HireEmployeeCommand(
            "12345678901",
            "",
            "Yılmaz",
            Guid.Empty,
            new DateOnly(2026, 1, 1),
            null,
            Guid.Empty,
            Nationality: "TUR"));

        result.Errors.Select(e => e.PropertyName).ShouldBe(["Tckn", "CompanyId", "FirstName", "Nationality", "EmploymentType", "PositionId"], ignoreOrder: true);
        result.Errors.Single(e => e.PropertyName == "Tckn").ErrorCode.ShouldBe("common.errors.invalid_tckn");
    }

    [Fact]
    public void Contacts_NeedValidValues_AndOnePrimaryAtMost()
    {
        var result = new UpdateEmployeeContactsCommandValidator().Validate(new UpdateEmployeeContactsCommand(
            1,
            [new EmailInput(ContactKind.Work, "ayse@", true), new EmailInput(ContactKind.Personal, "ayse@example.com", true)],
            [new PhoneInput(ContactKind.Mobile, "12", false)],
            [new AddressInput(ContactKind.Home, "", null, null, null, "Türkiye")],
            [new EmergencyContactInput("Mehmet Yılmaz", null, "0532 123 45 67")]));

        result.Errors.Select(e => e.PropertyName).ShouldBe(
            ["Emails", "Emails[0].Address", "Phones[0].Number", "Addresses[0].Line", "Addresses[0].CountryCode", "EmergencyContacts[0].Relation"],
            ignoreOrder: true);
    }

    [Fact]
    public void ChangeAssignment_CannotBeAHire() =>
        new ChangeAssignmentCommandValidator()
            .Validate(new ChangeAssignmentCommand(Guid.CreateVersion7(), new DateOnly(2026, 3, 1), AssignmentReason.Hire, Guid.CreateVersion7()))
            .Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Reason");

    [Theory]
    [InlineData(101, null, false)]
    [InlineData(40, "0 Rh+", true)]
    [InlineData(null, "Z Rh+", false)]
    public void SensitiveData_AcceptsKnownValues(int? degree, string? bloodType, bool valid) =>
        new UpdateEmployeeSensitiveDataCommandValidator()
            .Validate(new UpdateEmployeeSensitiveDataCommand(degree, bloodType, null, null))
            .IsValid.ShouldBe(valid);
}
