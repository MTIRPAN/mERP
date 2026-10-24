using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>A leave type a person may ask for, with what the request form needs (D44).</summary>
/// <param name="Id">The type.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name in the user's language.</param>
/// <param name="Category">Its kind.</param>
/// <param name="Unit">The smallest part of a request.</param>
/// <param name="DurationBasis">How its days are counted.</param>
/// <param name="DeductsFrom">The balance it uses.</param>
/// <param name="RequiresDocument">A document is attached when the request is sent.</param>
/// <param name="RequiresApproval">The request goes through approval.</param>
/// <param name="HasFixedLength">The type gives the last day (EVLILIK, BABALIK…); the form then asks for the event's day.</param>
public sealed record LeaveTypeOption(
    Guid Id,
    string Code,
    string Name,
    LeaveCategory Category,
    LeaveUnit Unit,
    DurationBasis DurationBasis,
    string? DeductsFrom,
    bool RequiresDocument,
    bool RequiresApproval,
    bool HasFixedLength);

/// <summary>The leave types the user may ask for; without <see cref="EmploymentId"/> those of the main employment.</summary>
public sealed record GetMyLeaveTypesQuery(Guid? EmploymentId = null) : IQuery<IReadOnlyList<LeaveTypeOption>>, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>The leave types an employment in the caller's scope may take, for HR's forms.</summary>
public sealed record GetLeaveRequestTypesQuery(Guid EmploymentId) : IQuery<IReadOnlyList<LeaveTypeOption>>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Requests.Read;
}

/// <summary>Colleagues who may stand in during the user's leave: people working in the user's companies, by name.</summary>
public sealed record GetMyColleaguesQuery(string? Search = null) : IQuery<IReadOnlyList<ColleagueItem>>, IRequirePermission
{
    public const int MaxResults = 20;

    public string Permission => HrPermissions.Self.LeaveRequest;
}

public sealed record ColleagueItem(Guid EmployeeId, string FullName);

internal static class LeaveTypeOptions
{
    /// <summary>Active types of the employment's company the person may ask for: no arrangements or travel, the right gender.</summary>
    public static async Task<IReadOnlyList<LeaveTypeOption>> ForAsync(
        IHrDbContext db,
        ICurrentUser currentUser,
        Employee employee,
        Employment employment,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var types = await db.LeaveTypes.AsNoTracking()
            .Where(t => t.IsActive && (t.CompanyId == null || t.CompanyId == employment.CompanyId)
                && t.Category != LeaveCategory.Arrangement && t.Category != LeaveCategory.Travel)
            .OrderBy(t => t.Code)
            .ToListAsync(cancellationToken);
        return
        [
            .. types
                .Where(t => t.GenderRestriction is null || t.GenderRestriction == employee.Gender)
                .Select(t => new LeaveTypeOption(
                    t.Id.Value,
                    t.Code,
                    t.Name.Get(currentUser.Culture, "tr-TR"),
                    t.Category,
                    t.Unit,
                    t.DurationBasis,
                    t.DeductsFrom,
                    t.RequiresDocument,
                    t.RequiresApproval,
                    t.VersionOn(today)?.FixedDays is > 0)),
        ];
    }
}

internal sealed class GetMyLeaveTypesQueryHandler(IHrDbContext db, ICurrentUser currentUser, IOrganizationLookup organization, TimeProvider timeProvider)
    : IQueryHandler<GetMyLeaveTypesQuery, IReadOnlyList<LeaveTypeOption>>
{
    public async Task<Result<IReadOnlyList<LeaveTypeOption>>> Handle(GetMyLeaveTypesQuery request, CancellationToken cancellationToken)
    {
        if (await MyLeave.EmploymentAsync(db, currentUser, request.EmploymentId, cancellationToken) is not { } me)
        {
            return CommonErrors.NotFound;
        }

        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, me.Employment.CompanyId, cancellationToken);
        return Result.Success(await LeaveTypeOptions.ForAsync(db, currentUser, me.Employee, me.Employment, today, cancellationToken));
    }
}

internal sealed class GetLeaveRequestTypesQueryHandler(
    IHrDbContext db,
    LeaveRequestAccess access,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : IQueryHandler<GetLeaveRequestTypesQuery, IReadOnlyList<LeaveTypeOption>>
{
    public async Task<Result<IReadOnlyList<LeaveTypeOption>>> Handle(GetLeaveRequestTypesQuery request, CancellationToken cancellationToken)
    {
        if (await access.EmploymentAsync(request.Permission, request.EmploymentId, cancellationToken) is not { } person)
        {
            return CommonErrors.NotFound;
        }

        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, person.Employment.CompanyId, cancellationToken);
        return Result.Success(await LeaveTypeOptions.ForAsync(db, currentUser, person.Employee, person.Employment, today, cancellationToken));
    }
}

internal sealed class GetMyColleaguesQueryHandler(IHrDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyColleaguesQuery, IReadOnlyList<ColleagueItem>>
{
    public async Task<Result<IReadOnlyList<ColleagueItem>>> Handle(GetMyColleaguesQuery request, CancellationToken cancellationToken)
    {
        var me = await SelfServiceRules.MeAsync(db, currentUser, tracked: false, cancellationToken);
        if (me is null)
        {
            return CommonErrors.NotFound;
        }

        var companies = me.Employments.Where(m => m.IsOpen).Select(m => m.CompanyId).Distinct().ToList();
        var people = db.Employees.AsNoTracking()
            .Where(e => e.Id != me.Id && e.Employments.Any(m => m.TerminationDate == null && companies.Contains(m.CompanyId)));
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            people = people.Where(e => TextSearch.Matches(e.FirstName + " " + e.LastName, pattern));
        }

        return await people
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Take(GetMyColleaguesQuery.MaxResults)
            .Select(e => new ColleagueItem(e.Id.Value, e.FirstName + " " + e.LastName))
            .ToListAsync(cancellationToken);
    }
}
