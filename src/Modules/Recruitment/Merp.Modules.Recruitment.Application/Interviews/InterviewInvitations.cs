using System.Globalization;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Interviews;

/// <summary>A person on a calendar invitation.</summary>
public sealed record CalendarAttendee(string Email, string? Name);

/// <summary>An interview as an iCalendar event (RFC 5545); a later version replaces it by <c>Uid</c> and <c>Sequence</c>.</summary>
public sealed record InterviewCalendarEvent(
    string Uid,
    int Sequence,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Summary,
    string? Location,
    string? Description,
    CalendarAttendee? Organizer,
    IReadOnlyList<CalendarAttendee> Attendees,
    bool Cancelled);

/// <summary>Writes the calendar file of an invitation (Ical.Net, D19); Infrastructure implements it.</summary>
public interface IInterviewCalendar
{
    /// <summary>The <c>.ics</c> content: a REQUEST, or a CANCEL for a cancelled interview.</summary>
    byte[] Build(InterviewCalendarEvent calendarEvent);
}

public enum InvitationKind
{
    Invite,
    Update,
    Cancel,
    Reminder,
}

/// <summary>Notifications of the interviewers (D52): in the bell and by e-mail with the calendar file.</summary>
public sealed class InterviewNotificationTypes : INotificationTypeProvider
{
    public const string Invited = "recruitment.interviewer_invited";
    public const string Updated = "recruitment.interviewer_updated";
    public const string Cancelled = "recruitment.interviewer_cancelled";
    public const string Reminder = "recruitment.interviewer_reminder";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(Invited, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(Updated, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(Cancelled, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(Reminder, [NotificationChannel.InApp, NotificationChannel.Email]),
    ];
}

/// <summary>
/// Sends the candidate and the interviewers the invitation of an interview, its update, cancellation or reminder (PLAN
/// R5, D52). Invitations, updates and cancellations carry the calendar file; the time is written in the company's time
/// zone and in the reader's language.
/// </summary>
public sealed class InterviewInvitations(
    IRecruitmentDbContext db,
    IOrganizationLookup organization,
    IUserDirectory users,
    IInterviewCalendar calendar,
    ILocalizationSource localization,
    ITextLocalizer localizer,
    IIntegrationEventPublisher publisher)
{
    public const string MyInterviewsLink = "/recruitment/my-interviews";

    /// <param name="interview">The interview as it is now.</param>
    /// <param name="kind">What happened.</param>
    /// <param name="removedInterviewers">Interviewers taken off an updated interview; they get the cancellation.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    public async Task SendAsync(Interview interview, InvitationKind kind, IReadOnlyList<Guid>? removedInterviewers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interview);
        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == interview.CandidateId, cancellationToken);
        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == interview.OpeningId, cancellationToken);
        var company = (await organization.GetCompaniesAsync([interview.CompanyId], cancellationToken)).SingleOrDefault();
        var defaultCulture = (await localization.GetAsync(cancellationToken)).DefaultCulture;
        var people = (await users.GetAsync([.. interview.InterviewerUserIds.Concat(removedInterviewers ?? []).Append(interview.OrganizerUserId).Distinct()], cancellationToken))
            .ToDictionary(u => u.Id);
        var candidateCulture = candidate.Culture ?? defaultCulture;
        var place = interview.Location ?? interview.OnlineLink ?? string.Empty;

        Dictionary<string, string> Args(string culture)
        {
            var local = RecruitmentClock.Local(interview.StartsAt, company?.TimeZoneId);
            var format = CultureInfo.GetCultureInfo(culture);
            return new Dictionary<string, string>
            {
                ["candidate"] = candidate.FullName,
                ["position"] = opening.Title,
                ["company"] = company?.ShortName ?? string.Empty,
                ["date"] = local.ToString("D", format),
                ["time"] = local.ToString("t", format),
                ["location"] = place,
                ["reason"] = interview.CancelReason ?? string.Empty,
            };
        }

        var organizer = people.GetValueOrDefault(interview.OrganizerUserId) is { Email: { } organizerEmail } organizerUser
            ? new CalendarAttendee(organizerEmail, organizerUser.DisplayName)
            : null;
        var attendees = interview.InterviewerUserIds
            .Select(id => people.GetValueOrDefault(id))
            .Where(u => u?.Email is not null)
            .Select(u => new CalendarAttendee(u!.Email!, u.DisplayName))
            .Prepend(new CalendarAttendee(candidate.Email, candidate.FullName))
            .ToList();

        EmailFile[] Invitation(string culture, bool cancelled) =>
            kind == InvitationKind.Reminder
                ? []
                :
                [
                    new EmailFile(
                        "interview.ics",
                        $"text/calendar; method={(cancelled ? "CANCEL" : "REQUEST")}; charset=utf-8",
                        calendar.Build(new InterviewCalendarEvent(
                            $"{interview.Id.Value}@merp",
                            interview.Sequence,
                            interview.StartsAt,
                            interview.EndsAt,
                            localizer.Get("recruitment.interviews.ics_summary", new Dictionary<string, object?> { ["position"] = opening.Title, ["company"] = company?.ShortName ?? string.Empty }, culture),
                            interview.Location,
                            interview.OnlineLink,
                            organizer,
                            attendees,
                            cancelled))),
                ];

        var candidateType = kind switch
        {
            InvitationKind.Invite => RecruitmentNotificationTypes.InterviewInvitation,
            InvitationKind.Update => RecruitmentNotificationTypes.InterviewUpdated,
            InvitationKind.Cancel => RecruitmentNotificationTypes.InterviewCancelled,
            _ => RecruitmentNotificationTypes.InterviewReminder,
        };
        await publisher.PublishAsync(
            new ExternalEmailRequested(candidateType, candidate.Email, candidate.FullName, candidateCulture, Args(candidateCulture))
            {
                CompanyId = interview.CompanyId,
                Attachments = Invitation(candidateCulture, kind == InvitationKind.Cancel),
            },
            cancellationToken);

        var interviewerType = kind switch
        {
            InvitationKind.Invite => InterviewNotificationTypes.Invited,
            InvitationKind.Update => InterviewNotificationTypes.Updated,
            InvitationKind.Cancel => InterviewNotificationTypes.Cancelled,
            _ => InterviewNotificationTypes.Reminder,
        };
        await publisher.PublishAsync(
            new NotificationRequested(interviewerType, interview.InterviewerUserIds, Args(defaultCulture), MyInterviewsLink)
            {
                CompanyId = interview.CompanyId,
                Attachments = Invitation(defaultCulture, kind == InvitationKind.Cancel),
            },
            cancellationToken);

        if (removedInterviewers is { Count: > 0 })
        {
            await publisher.PublishAsync(
                new NotificationRequested(InterviewNotificationTypes.Cancelled, removedInterviewers, Args(defaultCulture), MyInterviewsLink)
                {
                    CompanyId = interview.CompanyId,
                    Attachments = Invitation(defaultCulture, cancelled: true),
                },
                cancellationToken);
        }
    }
}
