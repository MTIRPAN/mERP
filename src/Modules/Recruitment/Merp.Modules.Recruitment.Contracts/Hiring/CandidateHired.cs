using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.Recruitment.Contracts.Hiring;

/// <summary>
/// The candidate of an application accepted the offer and the hiring team asked for the hire (PLAN R7, D54); HR opens a
/// pre-hire for it. IntegrationEvent.CompanyId is the company that hires.
/// </summary>
/// <param name="ApplicationId">The application; HR reports back with it.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="OfferId">The accepted offer.</param>
/// <param name="OrgUnitId">The unit of the opening's requisition; null without one.</param>
/// <param name="PositionId">The requisition's position; null for a new position or without a requisition.</param>
/// <param name="PositionTitle">The job offered.</param>
/// <param name="StartDate">The first working day of the offer.</param>
/// <param name="EmploymentType">The opening's kind of employment (Indefinite, FixedTerm, PartTime, Seasonal, Intern).</param>
/// <param name="FirstName">The candidate's first name.</param>
/// <param name="LastName">The candidate's last name.</param>
/// <param name="Email">The candidate's e-mail.</param>
/// <param name="Phone">The candidate's phone; null when none.</param>
/// <param name="EmployeeId">HR's employee when the candidate already works in the group (an internal candidate).</param>
/// <param name="RequestedByUserId">Who asked for the hire.</param>
public sealed record CandidateHired(
    Guid ApplicationId,
    Guid CandidateId,
    Guid OfferId,
    Guid? OrgUnitId,
    Guid? PositionId,
    string PositionTitle,
    DateOnly StartDate,
    string EmploymentType,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    Guid? EmployeeId,
    Guid? RequestedByUserId) : IntegrationEvent;
