using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.HR.Contracts.Employees;

// Integration events of the employee life cycle (PLAN H2, D38). IntegrationEvent.CompanyId is the company of the
// employment the event is about (for a group transfer, the target company).

/// <param name="EmployeeId">The person.</param>
/// <param name="EmploymentId">The new employment.</param>
/// <param name="RegistryNumber">Sicil no in the company.</param>
/// <param name="HireDate">First working day.</param>
/// <param name="PositionId">The first position.</param>
/// <param name="UserId">The person's platform user, when linked (H3).</param>
public sealed record EmployeeHired(Guid EmployeeId, Guid EmploymentId, string RegistryNumber, DateOnly HireDate, Guid PositionId, Guid? UserId)
    : IntegrationEvent;

/// <summary>Raised when a rehired person starts a new employment; the details are those of <see cref="EmployeeHired"/>.</summary>
public sealed record EmployeeRehired(Guid EmployeeId, Guid EmploymentId, string RegistryNumber, DateOnly HireDate, Guid PositionId, Guid? UserId)
    : IntegrationEvent;

/// <summary>
/// An assignment took effect: right away when it starts today or earlier, otherwise on its first day by the nightly
/// job <c>hr.assignments-effective</c>.
/// </summary>
/// <param name="EmployeeId">The person.</param>
/// <param name="EmploymentId">The employment.</param>
/// <param name="AssignmentId">The assignment.</param>
/// <param name="ValidFrom">First day of the assignment.</param>
/// <param name="PositionId">The position.</param>
/// <param name="OrgUnitId">The position's unit.</param>
/// <param name="Reason">Hire, Transfer, Promotion, Reorganization or Correction.</param>
public sealed record EmployeeAssignmentChanged(
    Guid EmployeeId,
    Guid EmploymentId,
    Guid AssignmentId,
    DateOnly ValidFrom,
    Guid PositionId,
    Guid OrgUnitId,
    string Reason) : IntegrationEvent;

/// <param name="EmployeeId">The person.</param>
/// <param name="EmploymentId">The ended employment.</param>
/// <param name="TerminationDate">Last working day.</param>
/// <param name="SgkExitCode">SGK leaving code.</param>
/// <param name="UserId">The person's platform user, when linked (H3).</param>
/// <param name="HasOtherEmployment">The person keeps another open employment, so their user stays active.</param>
public sealed record EmployeeTerminated(
    Guid EmployeeId,
    Guid EmploymentId,
    DateOnly TerminationDate,
    string SgkExitCode,
    Guid? UserId,
    bool HasOtherEmployment) : IntegrationEvent;

/// <param name="EmployeeId">The person.</param>
/// <param name="SourceEmploymentId">The ended employment.</param>
/// <param name="SourceCompanyId">Its company.</param>
/// <param name="TargetEmploymentId">The new employment.</param>
/// <param name="TransferDate">First day in the target company.</param>
/// <param name="SeniorityKept">The new employment keeps the source's seniority date.</param>
/// <param name="TransferLeaveBalance">The leave module moves the remaining leave balance (izin-kurallari.md).</param>
public sealed record EmployeeGroupTransferred(
    Guid EmployeeId,
    Guid SourceEmploymentId,
    Guid SourceCompanyId,
    Guid TargetEmploymentId,
    DateOnly TransferDate,
    bool SeniorityKept,
    bool TransferLeaveBalance) : IntegrationEvent;
