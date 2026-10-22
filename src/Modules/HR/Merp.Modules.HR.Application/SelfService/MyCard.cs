using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.SelfService;

/// <summary>
/// "Kartım": the card of the person linked to the user (D39). The TCKN and the IBAN are shown in plain, the person's
/// own data, and their read is audited; the special category data stays with HR (D13). 404 without a linked person.
/// </summary>
public sealed record GetMyEmployeeQuery : IQuery<EmployeeDetails>, IRequirePermission
{
    public string Permission => HrPermissions.Self.ProfileRead;
}

internal static class SelfServiceRules
{
    /// <summary>The person linked to the user, with employments and job history; null when there is none.</summary>
    public static async Task<Employee?> MeAsync(IHrDbContext db, ICurrentUser currentUser, bool tracked, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return null;
        }

        var employees = tracked ? db.Employees : db.Employees.AsNoTracking();
        return await employees
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleOrDefaultAsync(e => e.Id == EmployeeId.From(employeeId), cancellationToken);
    }
}

internal sealed class GetMyEmployeeQueryHandler(
    IHrDbContext db,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    ISensitiveDataProtector protector,
    ISensitiveReadLog readLog,
    IUserDirectory users,
    ITextLocalizer localizer,
    TimeProvider timeProvider) : IQueryHandler<GetMyEmployeeQuery, EmployeeDetails>
{
    public async Task<Result<EmployeeDetails>> Handle(GetMyEmployeeQuery request, CancellationToken cancellationToken)
    {
        var employee = await SelfServiceRules.MeAsync(db, currentUser, tracked: false, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var cards = new EmployeeCardBuilder(db, currentUser, organization, protector, readLog, users, localizer, timeProvider);
        return await cards.BuildAsync(
            employee.Id,
            [.. employee.Employments.Select(m => m.Id)],
            days,
            personSensitiveVisible: true,
            specialCategoryVisible: false,
            cancellationToken);
    }
}
