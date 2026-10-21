using Merp.Modules.HR.Domain.Documents;
using Merp.Modules.HR.Domain.Employees;

namespace Merp.Modules.HR.UnitTests.Documents;

public class EmployeeDocumentTests
{
    private static readonly DocumentTypeId Type = DocumentTypeId.From(Guid.CreateVersion7());
    private static readonly DateOnly Expiry = new(2026, 3, 31);

    private static EmployeeDocument Document(DateOnly? validUntil = null, DateOnly? issuedOn = null) =>
        EmployeeDocument.Create(EmployeeId.From(Guid.CreateVersion7()), new DocumentDetails(Type, " 12345 ", issuedOn, validUntil ?? Expiry, null)).Value;

    [Fact]
    public void Create_TrimsTheText_AndRejectsAValidityBeforeTheIssue()
    {
        Document().Number.ShouldBe("12345");

        var result = EmployeeDocument.Create(EmployeeId.From(Guid.CreateVersion7()), new DocumentDetails(Type, null, Expiry, Expiry.AddDays(-1), null));

        result.Error.ShouldBe(EmployeeDocumentErrors.ValidUntilBeforeIssue);
    }

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, 30)]
    [InlineData(8, 30)]
    [InlineData(7, 7)]
    [InlineData(0, 7)]
    [InlineData(-1, null)]
    public void DueReminder_IsTheClosestThresholdReached(int daysLeft, int? expected) =>
        Document().DueReminder(Expiry.AddDays(-daysLeft)).ShouldBe(expected);

    [Fact]
    public void DueReminder_SendsEachReminderOnce_AndTheSevenDayOneAfterTheThirtyDayOne()
    {
        var document = Document();
        var thirtyDaysBefore = Expiry.AddDays(-30);

        document.MarkReminded(document.DueReminder(thirtyDaysBefore)!.Value);

        document.DueReminder(thirtyDaysBefore.AddDays(1)).ShouldBeNull();
        document.DueReminder(Expiry.AddDays(-7)).ShouldBe(7);
        document.MarkReminded(7);
        document.DueReminder(Expiry.AddDays(-2)).ShouldBeNull();
    }

    [Fact]
    public void DueReminder_ForADocumentFoundLate_SkipsTheThirtyDayReminder()
    {
        var document = Document();

        document.MarkReminded(document.DueReminder(Expiry.AddDays(-5))!.Value);

        document.RemindedDaysBefore.ShouldBe(7);
        document.DueReminder(Expiry.AddDays(-3)).ShouldBeNull();
    }

    [Fact]
    public void Update_WithANewValidity_RestartsTheReminders_AndADocumentWithoutOneHasNone()
    {
        var document = Document();
        document.MarkReminded(7);

        document.Update(new DocumentDetails(Type, null, null, Expiry, "Aynı tarih")).IsSuccess.ShouldBeTrue();
        document.RemindedDaysBefore.ShouldBe(7);

        var renewed = Expiry.AddYears(1);
        document.Update(new DocumentDetails(Type, null, null, renewed, null)).IsSuccess.ShouldBeTrue();
        document.RemindedDaysBefore.ShouldBeNull();
        document.DueReminder(renewed.AddDays(-30)).ShouldBe(30);

        document.Update(new DocumentDetails(Type, null, null, null, null));
        document.DueReminder(renewed.AddDays(-1)).ShouldBeNull();
    }
}
