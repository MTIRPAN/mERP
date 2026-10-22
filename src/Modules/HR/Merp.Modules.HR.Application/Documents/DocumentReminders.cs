using System.Globalization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Modules.HR.Domain.Documents;
using Merp.Modules.Platform.Contracts.Access;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Documents;

/// <summary>Notification types of the HR module (D32); texts are <c>hr.notifications.{name}.*</c>.</summary>
public sealed class HrNotificationTypes : INotificationTypeProvider
{
    /// <summary>An employee document expires in 30 or 7 days (D38).</summary>
    public const string DocumentExpiring = "hr.document_expiring";

    /// <summary>A change request of the user ended: written to the card, kept off it or rejected (D39).</summary>
    public const string ProfileChangeCompleted = "hr.profile_change_completed";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(DocumentExpiring, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(ProfileChangeCompleted, [NotificationChannel.InApp, NotificationChannel.Email]),
    ];
}

/// <summary>
/// Reminds HR of the documents that expire in 30 or 7 days (job <c>hr.document-reminders</c>, D38). The recipients
/// hold <c>hr.employees.documents.manage</c> in a company where the person works or is about to start; documents of
/// people who left are not reminded. Each reminder is sent once per validity date. Returns how many were sent.
/// </summary>
public sealed record SendDocumentRemindersCommand : ICommand<int>, IBulkCommand;

internal sealed class SendDocumentRemindersCommandHandler(
    IHrDbContext db,
    IOrganizationLookup organization,
    IPermissionHolders permissionHolders,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<SendDocumentRemindersCommand, int>
{
    private const string Culture = "tr-TR";

    public async Task<Result<int>> Handle(SendDocumentRemindersCommand request, CancellationToken cancellationToken)
    {
        // Every company is within a day of UTC, and the earliest reminder is 30 days ahead
        var utc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = utc.AddDays(-1);
        var until = utc.AddDays(EmployeeDocument.ReminderDays.Max() + 1);
        var documents = await db.EmployeeDocuments
            .Where(d => d.ValidUntil != null && d.ValidUntil >= from && d.ValidUntil <= until)
            .ToListAsync(cancellationToken);
        if (documents.Count == 0)
        {
            return 0;
        }

        var employeeIds = documents.Select(d => d.EmployeeId).Distinct().ToList();
        var people = await db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                Employments = e.Employments.Select(m => new { m.CompanyId, m.HireDate, m.TerminationDate, m.IsPrimary }).ToList(),
            })
            .ToListAsync(cancellationToken);
        var zones = (await organization.GetCompaniesAsync([.. people.SelectMany(p => p.Employments).Select(m => m.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.TimeZoneId);
        var typeIds = documents.Select(d => d.TypeId).Distinct().ToList();
        var typeNames = (await db.DocumentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToListAsync(cancellationToken))
            .ToDictionary(t => t.Id, t => t.Name.Get(Culture, Culture));
        var holders = new Dictionary<Guid, IReadOnlyList<Guid>>();

        var sent = 0;
        foreach (var document in documents)
        {
            var person = people.Single(p => p.Id == document.EmployeeId);

            // Companies where the person works today or is about to start, the primary employment first
            var companies = person.Employments
                .Where(m => m.TerminationDate is not { } end || end >= HrClock.Today(timeProvider, zones.GetValueOrDefault(m.CompanyId)))
                .OrderByDescending(m => m.IsPrimary)
                .ThenBy(m => m.HireDate)
                .Select(m => m.CompanyId)
                .ToList();
            if (companies.Count == 0)
            {
                continue;
            }

            var today = HrClock.Today(timeProvider, zones.GetValueOrDefault(companies[0]));
            if (document.DueReminder(today) is not { } daysBefore)
            {
                continue;
            }

            var recipients = new List<Guid>();
            foreach (var companyId in companies)
            {
                if (!holders.TryGetValue(companyId, out var users))
                {
                    users = await permissionHolders.GetUserIdsAsync(HrPermissions.Employees.Documents.Manage, companyId, cancellationToken);
                    holders[companyId] = users;
                }

                recipients.AddRange(users);
            }

            if (recipients.Count == 0)
            {
                continue;
            }

            var validUntil = document.ValidUntil!.Value;
            await publisher.PublishAsync(
                new NotificationRequested(
                    HrNotificationTypes.DocumentExpiring,
                    [.. recipients.Distinct()],
                    new Dictionary<string, string>
                    {
                        ["employee"] = $"{person.FirstName} {person.LastName}",
                        ["document"] = typeNames.GetValueOrDefault(document.TypeId) ?? string.Empty,
                        ["date"] = validUntil.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                        ["days"] = (validUntil.DayNumber - today.DayNumber).ToString(CultureInfo.InvariantCulture),
                    },
                    $"/hr/employees/{person.Id.Value}?tab=documents")
                {
                    CompanyId = companies[0],
                },
                cancellationToken);
            document.MarkReminded(daysBefore);
            sent++;
        }

        return sent;
    }
}
