using Merp.Modules.HR.Application.Imports;
using Merp.Modules.HR.Domain.Employees;

namespace Merp.Modules.HR.UnitTests.Imports;

/// <summary>The primary e-mail of a known person in the update mode (D40).</summary>
public sealed class EmployeeImportRowsTests
{
    private static List<EmployeeEmail> WithPrimary(IReadOnlyList<EmployeeEmail> current, string? address) =>
        EmployeeImportRows.WithPrimary(current, address, e => e.Address, (e, primary) => e with { IsPrimary = primary }, a => new EmployeeEmail(ContactKind.Work, a, true));

    [Fact]
    public void NewAddress_BecomesThePrimary_AndTheOthersStay()
    {
        var emails = WithPrimary(
            [new EmployeeEmail(ContactKind.Work, "eski@acme.test", true), new EmployeeEmail(ContactKind.Personal, "ev@posta.test", false)],
            "yeni@acme.test");

        emails.ShouldBe(
        [
            new EmployeeEmail(ContactKind.Work, "yeni@acme.test", true),
            new EmployeeEmail(ContactKind.Work, "eski@acme.test", false),
            new EmployeeEmail(ContactKind.Personal, "ev@posta.test", false),
        ]);
    }

    [Fact]
    public void KnownAddress_IsMarkedPrimary_KeepingItsKind()
    {
        var emails = WithPrimary(
            [new EmployeeEmail(ContactKind.Work, "is@acme.test", true), new EmployeeEmail(ContactKind.Personal, "EV@posta.test", false)],
            "ev@posta.test");

        emails.ShouldBe(
        [
            new EmployeeEmail(ContactKind.Personal, "EV@posta.test", true),
            new EmployeeEmail(ContactKind.Work, "is@acme.test", false),
        ]);
    }

    [Fact]
    public void BlankCell_KeepsTheList()
    {
        var current = new[] { new EmployeeEmail(ContactKind.Work, "is@acme.test", true) };

        WithPrimary(current, null).ShouldBe(current);
    }
}
