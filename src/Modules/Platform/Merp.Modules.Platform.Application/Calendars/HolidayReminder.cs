using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Calendars;

/// <summary>
/// Reminds the calendar administrators when the religious holidays of <paramref name="Year"/> are missing from a
/// holiday calendar (PLAN P9; job <c>calendars.holiday-reminder</c>, every 1 October). Returns how many calendars lack them.
/// </summary>
public sealed record RemindMissingHolidaysCommand(int Year) : ICommand<int>;

internal sealed class RemindMissingHolidaysCommandHandler(IPlatformDbContext db, IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : ICommandHandler<RemindMissingHolidaysCommand, int>
{
    public async Task<Result<int>> Handle(RemindMissingHolidaysCommand request, CancellationToken cancellationToken)
    {
        var missing = await db.HolidayCalendars.AsNoTracking()
            .Where(c => !c.Holidays.Any(h => h.Date.Year == request.Year && h.IsMovable))
            .Select(c => new { c.Id, c.Code })
            .ToListAsync(cancellationToken);
        if (missing.Count == 0)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var recipients = await PermissionHolders.UserIdsAsync(db, PlatformPermissions.Calendars.Manage, today, cancellationToken);
        if (recipients.Count == 0)
        {
            return missing.Count;
        }

        foreach (var calendar in missing)
        {
            await publisher.PublishAsync(
                new NotificationRequested(
                    PlatformNotificationTypes.HolidaysMissing,
                    recipients,
                    new Dictionary<string, string>
                    {
                        ["year"] = request.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["calendar"] = calendar.Code,
                    },
                    $"/system/holidays?calendar={calendar.Id.Value}&year={request.Year}"),
                cancellationToken);
        }

        return missing.Count;
    }
}

/// <summary>Users who hold a permission today with the Tenant scope, e.g. to notify administrators.</summary>
internal static class PermissionHolders
{
    public static async Task<IReadOnlyList<Guid>> UserIdsAsync(IPlatformDbContext db, string permission, DateOnly today, CancellationToken cancellationToken)
    {
        var roles = db.Roles.Where(r => r.Permissions.Any(p => p.PermissionCode == permission)).Select(r => r.Id);
        return await db.Users
            .Where(u => u.IsActive && u.Assignments.Any(a =>
                roles.Contains(a.RoleId)
                && a.ScopeType == AccessScopeTypes.Tenant
                && (a.ValidFrom == null || a.ValidFrom <= today)
                && (a.ValidTo == null || a.ValidTo >= today)))
            .Select(u => u.Id.Value)
            .ToListAsync(cancellationToken);
    }
}
