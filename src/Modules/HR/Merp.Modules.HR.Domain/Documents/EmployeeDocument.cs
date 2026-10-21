using Merp.Modules.HR.Domain.Employees;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Domain.Documents;

public readonly record struct EmployeeDocumentId(Guid Value) : IEntityId<EmployeeDocumentId>
{
    public static EmployeeDocumentId From(Guid value) => new(value);
}

/// <summary>What a document says: its type, number and validity.</summary>
public sealed record DocumentDetails(DocumentTypeId TypeId, string? Number, DateOnly? IssuedOn, DateOnly? ValidUntil, string? Note);

/// <summary>
/// A document of an employee (D38): its own aggregate, so uploading documents never conflicts with editing the card.
/// The file is a stored file of the document (D31). HR is reminded 30 and 7 days before the document expires; each
/// reminder is sent once per validity date.
/// </summary>
[Audited]
public sealed class EmployeeDocument : AuditableAggregateRoot<EmployeeDocumentId>, ISoftDeletable
{
    public const int NumberMaxLength = 50;
    public const int NoteMaxLength = 500;

    /// <summary>Days before the end of validity on which HR is reminded, the earliest first.</summary>
    public static readonly IReadOnlyList<int> ReminderDays = [30, 7];

    private EmployeeDocument()
    {
    }

    public EmployeeId EmployeeId { get; private set; }

    public DocumentTypeId TypeId { get; private set; }

    public string? Number { get; private set; }

    public DateOnly? IssuedOn { get; private set; }

    /// <summary>Last day of validity; null when the document does not expire.</summary>
    public DateOnly? ValidUntil { get; private set; }

    public string? Note { get; private set; }

    /// <summary>The reminder last sent for <see cref="ValidUntil"/>, in days before it (30 or 7).</summary>
    public int? RemindedDaysBefore { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public static Result<EmployeeDocument> Create(EmployeeId employeeId, DocumentDetails details)
    {
        var document = new EmployeeDocument { Id = EntityId.New<EmployeeDocumentId>(), EmployeeId = employeeId };
        var applied = document.Update(details);
        return applied.IsSuccess ? document : applied.Error;
    }

    public Result Update(DocumentDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.IssuedOn is { } issued && details.ValidUntil is { } until && until < issued)
        {
            return EmployeeDocumentErrors.ValidUntilBeforeIssue;
        }

        if (details.ValidUntil != ValidUntil)
        {
            RemindedDaysBefore = null;
        }

        TypeId = details.TypeId;
        Number = Text.Optional(details.Number, NumberMaxLength, nameof(details.Number));
        IssuedOn = details.IssuedOn;
        ValidUntil = details.ValidUntil;
        Note = Text.Optional(details.Note, NoteMaxLength, nameof(details.Note));
        return Result.Success();
    }

    /// <summary>
    /// The reminder due on <paramref name="today"/>, in days before the end of validity: the closest threshold the
    /// document has reached and not been reminded of. A document that expired or does not expire has none.
    /// </summary>
    public int? DueReminder(DateOnly today)
    {
        if (ValidUntil is not { } until || until < today)
        {
            return null;
        }

        var daysLeft = until.DayNumber - today.DayNumber;
        var reached = ReminderDays.Where(d => daysLeft <= d).Order().FirstOrDefault();
        return reached == 0 || (RemindedDaysBefore is { } sent && sent <= reached) ? null : reached;
    }

    public void MarkReminded(int daysBefore) => RemindedDaysBefore = daysBefore;
}

public static class EmployeeDocumentErrors
{
    public static readonly Error ValidUntilBeforeIssue = Error.Validation([new FieldError("validUntil", "hr.documents.errors.valid_until_before_issue")]);
}
