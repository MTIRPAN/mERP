using System.Text.Json.Serialization;
using Merp.Modules.HR.Domain.Employees;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Domain.SelfService;

public readonly record struct ProfileChangeRequestId(Guid Value) : IEntityId<ProfileChangeRequestId>
{
    public static ProfileChangeRequestId From(Guid value) => new(value);
}

/// <summary>The section of the card a change request replaces (D39).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProfileChangeKind>))]
public enum ProfileChangeKind
{
    /// <summary>E-mail addresses, phones, addresses and emergency contacts together.</summary>
    Contacts,

    Family,

    Iban,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProfileChangeStatus>))]
public enum ProfileChangeStatus
{
    /// <summary>Waits for approval.</summary>
    Pending,

    /// <summary>An approver sent it back for correction; the requester resubmits or withdraws it.</summary>
    Returned,

    /// <summary>Approved and written to the card.</summary>
    Applied,

    /// <summary>Approved, but the card's section changed since the request: HR's later edit is kept.</summary>
    NotApplied,

    Rejected,

    Withdrawn,
}

/// <summary>
/// A person's request to change a section of their card (değişiklik talebi, D39): sent for approval as
/// <c>hr.profile-change</c> and written to the card when approved, unless the card's section changed meanwhile. The
/// proposed and the former values are JSON in the shape of the card's lists; an IBAN is kept in its own sensitive
/// fields.
/// </summary>
[Audited]
public sealed class ProfileChangeRequest : AuditableAggregateRoot<ProfileChangeRequestId>
{
    public const int NumberMaxLength = 30;
    public const int NoteMaxLength = 500;

    private ProfileChangeRequest()
    {
    }

    /// <summary>P5 <c>hr.profile-change</c>, e.g. PDT-2026-00012.</summary>
    public string Number { get; private set; } = null!;

    public EmployeeId EmployeeId { get; private set; }

    /// <summary>Company whose flow approves it: the person's main employment when the request was sent.</summary>
    public Guid CompanyId { get; private set; }

    public Guid RequesterUserId { get; private set; }

    public ProfileChangeKind Kind { get; private set; }

    public ProfileChangeStatus Status { get; private set; }

    /// <summary>The proposed section as JSON; null for an IBAN change.</summary>
    public string? Proposed { get; private set; }

    /// <summary>The section as it was when the request was (re)sent, as JSON; null for an IBAN change.</summary>
    public string? Before { get; private set; }

    [Sensitive]
    public string? ProposedIban { get; private set; }

    [Sensitive]
    public string? BeforeIban { get; private set; }

    /// <summary>The requester's explanation.</summary>
    public string? Note { get; private set; }

    /// <summary>Why an approver returned it.</summary>
    public string? ReturnComment { get; private set; }

    /// <summary>When the flow ended or the requester withdrew it.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    public bool IsOpen => Status is ProfileChangeStatus.Pending or ProfileChangeStatus.Returned;

    public static ProfileChangeRequest Submit(
        string number,
        EmployeeId employeeId,
        Guid companyId,
        Guid requesterUserId,
        ProfileChangeKind kind,
        ProfileChangeValues values,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind.");
        }

        var request = new ProfileChangeRequest
        {
            Id = EntityId.New<ProfileChangeRequestId>(),
            Number = Text.Required(number, NumberMaxLength, nameof(number)),
            EmployeeId = employeeId,
            CompanyId = companyId,
            RequesterUserId = requesterUserId,
            Kind = kind,
        };
        request.Set(values, note);
        return request;
    }

    /// <summary>The requester corrects a returned request; it waits for approval again.</summary>
    public Result Resubmit(ProfileChangeValues values, string? note)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (Status != ProfileChangeStatus.Returned)
        {
            return ProfileChangeErrors.NotReturned;
        }

        Set(values, note);
        return Result.Success();
    }

    public Result Withdraw(DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return ProfileChangeErrors.Closed;
        }

        Status = ProfileChangeStatus.Withdrawn;
        ClosedAt = at;
        return Result.Success();
    }

    /// <summary>An approver returned it (<c>ApprovalRequestReturned</c>); ignored once closed.</summary>
    public void Return(string comment)
    {
        if (Status == ProfileChangeStatus.Pending)
        {
            Status = ProfileChangeStatus.Returned;
            ReturnComment = Text.Optional(comment, NoteMaxLength, nameof(comment));
        }
    }

    /// <summary>The flow ended (<c>ApprovalRequestCompleted</c>): written to the card, kept off it, or rejected.</summary>
    public void Close(ProfileChangeStatus outcome, DateTimeOffset at)
    {
        if (outcome is not (ProfileChangeStatus.Applied or ProfileChangeStatus.NotApplied or ProfileChangeStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A flow ends applied, not applied or rejected.");
        }

        if (!IsOpen)
        {
            return;
        }

        Status = outcome;
        ClosedAt = at;
        if (outcome == ProfileChangeStatus.Applied)
        {
            Raise(new ProfileChangeApproved(Id, EmployeeId, Kind, Number, Proposed, ProposedIban));
        }
    }

    private void Set(ProfileChangeValues values, string? note)
    {
        var iban = Kind == ProfileChangeKind.Iban;
        Proposed = iban ? null : values.Proposed ?? throw new ArgumentException("The proposed section is required.", nameof(values));
        Before = iban ? null : values.Before;
        ProposedIban = iban ? values.ProposedIban : null;
        BeforeIban = iban ? values.BeforeIban : null;
        Note = Text.Optional(note, NoteMaxLength, nameof(note));
        ReturnComment = null;
        Status = ProfileChangeStatus.Pending;
    }
}

/// <summary>An approved request is written to the card in the same transaction (CLAUDE.md rule 7).</summary>
public sealed record ProfileChangeApproved(
    ProfileChangeRequestId RequestId,
    EmployeeId EmployeeId,
    ProfileChangeKind Kind,
    string Number,
    string? Proposed,
    string? ProposedIban) : IDomainEvent;

/// <summary>What a request proposes and what the card held; JSON sections, or the IBAN values.</summary>
public sealed record ProfileChangeValues(string? Proposed, string? Before, string? ProposedIban, string? BeforeIban);

public static class ProfileChangeErrors
{
    public static readonly Error NotReturned = Error.Conflict("hr.profile_changes.errors.not_returned");
    public static readonly Error Closed = Error.Conflict("hr.profile_changes.errors.closed");
}
