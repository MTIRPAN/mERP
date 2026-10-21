using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// Announces the assignments that have started but were entered for a later day: <c>EmployeeAssignmentChanged</c>
/// on their first day in the company's time zone (job <c>hr.assignments-effective</c>, D38). Returns how many.
/// </summary>
public sealed record AnnounceStartedAssignmentsCommand : ICommand<int>, IBulkCommand;

internal sealed class AnnounceStartedAssignmentsCommandHandler(
    IHrDbContext db,
    IOrganizationLookup organization,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<AnnounceStartedAssignmentsCommand, int>
{
    public async Task<Result<int>> Handle(AnnounceStartedAssignmentsCommand request, CancellationToken cancellationToken)
    {
        // A day after today in UTC: no time zone is that far ahead
        var horizon = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);
        var candidates = await db.Employees.AsNoTracking()
            .SelectMany(e => e.Employments, (e, m) => new { Employee = e, Employment = m })
            .SelectMany(x => x.Employment.Assignments, (x, a) => new { x.Employee, x.Employment, Assignment = a })
            .Where(x => !x.Assignment.Announced && x.Assignment.ValidFrom <= horizon)
            .Select(x => new { EmployeeId = x.Employee.Id.Value, x.Employment.CompanyId, AssignmentId = x.Assignment.Id, x.Assignment.ValidFrom })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var zones = (await organization.GetCompaniesAsync([.. candidates.Select(c => c.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.TimeZoneId);
        var started = candidates.Where(c => c.ValidFrom <= HrClock.Today(timeProvider, zones.GetValueOrDefault(c.CompanyId))).ToList();
        foreach (var group in started.GroupBy(c => c.EmployeeId))
        {
            var employee = (await EmployeeRules.LoadAsync(db, group.Key, cancellationToken))!;
            foreach (var candidate in group)
            {
                var employment = employee.Employments.Single(m => m.Assignments.Any(a => a.Id == candidate.AssignmentId));
                var assignment = employment.Assignments.Single(a => a.Id == candidate.AssignmentId);
                await EmployeeEvents.PublishAsync(db, publisher, employee, employment, assignment, cancellationToken);
            }
        }

        return started.Count;
    }
}
