using System.Text.Json.Serialization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Domain.PreHires;

public readonly record struct PreHireId(Guid Value) : IEntityId<PreHireId>
{
    public static PreHireId From(Guid value) => new(value);
}

/// <summary>Where a pre-hire stands (D54).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreHireStatus>))]
public enum PreHireStatus
{
    /// <summary>Waits for HR to complete the hire.</summary>
    Pending,

    /// <summary>The person was hired, rehired or given the new assignment.</summary>
    Completed,

    /// <summary>HR called it off, e.g. the candidate did not start.</summary>
    Cancelled,
}

/// <summary>What Recruitment knows of a hire when the candidate accepted the offer.</summary>
/// <param name="ApplicationId">Recruitment's application.</param>
/// <param name="CandidateId">Recruitment's candidate.</param>
/// <param name="OfferId">Recruitment's accepted offer.</param>
/// <param name="CompanyId">The company that hires.</param>
/// <param name="OrgUnitId">The unit of the requisition; null without one.</param>
/// <param name="PositionId">The requisition's position; null for a new position.</param>
/// <param name="PositionTitle">The job offered.</param>
/// <param name="StartDate">The first working day of the offer.</param>
/// <param name="EmploymentType">The kind of employment.</param>
/// <param name="FirstName">The candidate's first name.</param>
/// <param name="LastName">The candidate's last name.</param>
/// <param name="Email">The candidate's e-mail.</param>
/// <param name="Phone">The candidate's phone.</param>
/// <param name="EmployeeId">The person when they already work in the group: Recruitment's link or the e-mail match.</param>
public sealed record PreHireDetails(
    Guid ApplicationId,
    Guid CandidateId,
    Guid OfferId,
    Guid CompanyId,
    OrgUnitId? OrgUnitId,
    PositionId? PositionId,
    string PositionTitle,
    DateOnly StartDate,
    EmploymentType EmploymentType,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    EmployeeId? EmployeeId);

/// <summary>
/// İşe giriş ön kaydı (PLAN R7, D54): a candidate who accepted an offer, waiting for HR to complete the hire with the
/// TCKN, the position and the registry details. Completing it hires a new person, rehires a former one or gives an
/// employee of the company the new assignment; the employee aggregate does that and this one follows by a domain event.
/// </summary>
[Audited]
public sealed class PreHire : AuditableAggregateRoot<PreHireId>, ICompanyScoped
{
    public const int NameMaxLength = 100;
    public const int TitleMaxLength = 150;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int ReasonMaxLength = 500;

    private PreHire()
    {
    }

    /// <summary>One pre-hire per application.</summary>
    public Guid ApplicationId { get; private set; }

    public Guid CandidateId { get; private set; }

    public Guid OfferId { get; private set; }

    public Guid CompanyId { get; private set; }

    public OrgUnitId? OrgUnitId { get; private set; }

    /// <summary>The position suggested; HR picks the position when completing.</summary>
    public PositionId? PositionId { get; private set; }

    public string PositionTitle { get; private set; } = null!;

    public DateOnly StartDate { get; private set; }

    public EmploymentType EmploymentType { get; private set; }

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public string? Phone { get; private set; }

    /// <summary>The person when they already work, or worked, in the group.</summary>
    public EmployeeId? ExistingEmployeeId { get; private set; }

    public PreHireStatus Status { get; private set; }

    /// <summary>The person the hire made or used.</summary>
    public EmployeeId? EmployeeId { get; private set; }

    public EmploymentId? EmploymentId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public static PreHire Open(PreHireDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new PreHire
        {
            Id = EntityId.New<PreHireId>(),
            ApplicationId = details.ApplicationId,
            CandidateId = details.CandidateId,
            OfferId = details.OfferId,
            CompanyId = details.CompanyId,
            OrgUnitId = details.OrgUnitId,
            PositionId = details.PositionId,
            PositionTitle = Text.Required(details.PositionTitle, TitleMaxLength, nameof(details.PositionTitle)),
            StartDate = details.StartDate,
            EmploymentType = details.EmploymentType,
            FirstName = Text.Required(details.FirstName, NameMaxLength, nameof(details.FirstName)),
            LastName = Text.Required(details.LastName, NameMaxLength, nameof(details.LastName)),
            Email = Text.Required(details.Email, EmailMaxLength, nameof(details.Email)),
            Phone = Text.Optional(details.Phone, PhoneMaxLength, nameof(details.Phone)),
            ExistingEmployeeId = details.EmployeeId,
            Status = PreHireStatus.Pending,
        };
    }

    /// <summary>The hire was completed for <paramref name="employeeId"/>; ignored unless the pre-hire waits.</summary>
    public bool Complete(EmployeeId employeeId, EmploymentId employmentId, DateTimeOffset at)
    {
        if (Status != PreHireStatus.Pending)
        {
            return false;
        }

        Status = PreHireStatus.Completed;
        EmployeeId = employeeId;
        EmploymentId = employmentId;
        CompletedAt = at;
        return true;
    }

    /// <summary>HR calls the hire off.</summary>
    public Result Cancel(string? reason)
    {
        if (Status != PreHireStatus.Pending)
        {
            return PreHireErrors.NotPending;
        }

        Status = PreHireStatus.Cancelled;
        CancelReason = Text.Optional(reason, ReasonMaxLength, nameof(reason));
        return Result.Success();
    }
}

/// <summary>
/// An employment or assignment of the person completed a pre-hire (D54): raised by the employee aggregate, so the
/// pre-hire follows in the same transaction (CLAUDE.md rule 7).
/// </summary>
public sealed record PreHireConfirmed(PreHireId PreHireId, EmployeeId EmployeeId, EmploymentId EmploymentId, DateOnly StartDate) : IDomainEvent;

public static class PreHireErrors
{
    public const string UnknownCode = "hr.pre_hires.errors.unknown";
    public const string OtherPersonCode = "hr.pre_hires.errors.other_person";

    public static readonly Error NotPending = Error.Conflict("hr.pre_hires.errors.not_pending");
}
