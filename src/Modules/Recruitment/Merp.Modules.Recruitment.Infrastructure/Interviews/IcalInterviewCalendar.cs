using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Merp.Modules.Recruitment.Application.Interviews;

namespace Merp.Modules.Recruitment.Infrastructure.Interviews;

/// <summary>
/// The calendar file of an interview invitation (RFC 5545, Ical.Net): times in UTC, the interview's id as UID and its
/// SEQUENCE, so calendars replace or remove the earlier version (D52).
/// </summary>
public sealed class IcalInterviewCalendar : IInterviewCalendar
{
    public byte[] Build(InterviewCalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        var calendar = new Calendar { Method = calendarEvent.Cancelled ? "CANCEL" : "REQUEST" };
        var interview = new CalendarEvent
        {
            Uid = calendarEvent.Uid,
            Sequence = calendarEvent.Sequence,
            DtStart = new CalDateTime(calendarEvent.StartsAt.UtcDateTime, "UTC"),
            DtEnd = new CalDateTime(calendarEvent.EndsAt.UtcDateTime, "UTC"),
            Summary = calendarEvent.Summary,
            Location = calendarEvent.Location,
            Description = calendarEvent.Description,
            Status = calendarEvent.Cancelled ? EventStatus.Cancelled : EventStatus.Confirmed,
        };
        if (calendarEvent.Organizer is { } organizer)
        {
            interview.Organizer = new Organizer($"mailto:{organizer.Email}") { CommonName = organizer.Name };
        }

        foreach (var attendee in calendarEvent.Attendees)
        {
            interview.Attendees.Add(new Attendee($"mailto:{attendee.Email}")
            {
                CommonName = attendee.Name,
                Role = ParticipationRole.RequiredParticipant,
                ParticipationStatus = EventParticipationStatus.NeedsAction,
                Rsvp = true,
            });
        }

        calendar.Events.Add(interview);
        return Encoding.UTF8.GetBytes(new CalendarSerializer().SerializeToString(calendar) ?? string.Empty);
    }
}
