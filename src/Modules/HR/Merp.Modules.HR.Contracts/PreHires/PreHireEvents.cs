using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.HR.Contracts.PreHires;

// Integration events of pre-hires (PLAN R7, D54): HR reports back to Recruitment on the application of the hire.
// IntegrationEvent.CompanyId is the company of the pre-hire.

/// <summary>HR completed the pre-hire: the person was hired, rehired or given the new assignment.</summary>
/// <param name="ApplicationId">The application the pre-hire came from.</param>
/// <param name="EmployeeId">The person.</param>
/// <param name="EmploymentId">The employment the person works in now.</param>
/// <param name="StartDate">The first day of the employment or of the new assignment.</param>
public sealed record PreHireCompleted(Guid ApplicationId, Guid EmployeeId, Guid EmploymentId, DateOnly StartDate) : IntegrationEvent;

/// <summary>HR cancelled the pre-hire, e.g. the candidate did not start; the application goes back to the pipeline.</summary>
/// <param name="ApplicationId">The application the pre-hire came from.</param>
/// <param name="Reason">Why, as HR wrote it.</param>
public sealed record PreHireCancelled(Guid ApplicationId, string? Reason) : IntegrationEvent;
