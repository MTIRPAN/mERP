using System.Text;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Infrastructure.Interviews;

namespace Merp.Modules.Recruitment.UnitTests.Interviews;

/// <summary>The calendar file of an interview invitation (RFC 5545, D52).</summary>
public sealed class InterviewCalendarTests
{
    private static readonly InterviewCalendarEvent Event = new(
        "0199e2a4-7c1b-7d2e-9f00-1234567890ab@merp",
        0,
        new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(3)),
        new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.FromHours(3)),
        "Mülakat: Yazılım Geliştirici",
        null,
        "https://meet.example/abc",
        new CalendarAttendee("ik@acme.test", "İK Uzmanı"),
        [new CalendarAttendee("aday.uzun.bir.eposta.adresi@ornek.test", "Ayşe Yılmaz"), new CalendarAttendee("ayse@acme.test", null)],
        Cancelled: false);

    /// <summary>The file with its folded lines joined (RFC 5545 §3.1).</summary>
    private static string Build(InterviewCalendarEvent calendarEvent) =>
        Encoding.UTF8.GetString(new IcalInterviewCalendar().Build(calendarEvent)).Replace("\r\n ", string.Empty, StringComparison.Ordinal);

    [Fact]
    public void AnInvitation_IsARequest_InUtc_WithTheInterviewAsUidAndItsSequence()
    {
        var ics = Build(Event);

        ics.ShouldSatisfyAllConditions(
            c => c.ShouldContain("BEGIN:VCALENDAR"),
            c => c.ShouldContain("METHOD:REQUEST"),
            c => c.ShouldContain("UID:0199e2a4-7c1b-7d2e-9f00-1234567890ab@merp"),
            c => c.ShouldContain("SEQUENCE:0"),
            c => c.ShouldContain("DTSTART:20261105T070000Z"),
            c => c.ShouldContain("DTEND:20261105T080000Z"),
            c => c.ShouldContain("SUMMARY:Mülakat: Yazılım Geliştirici"),
            c => c.ShouldContain("mailto:aday.uzun.bir.eposta.adresi@ornek.test"),
            c => c.ShouldContain("mailto:ayse@acme.test"),
            c => c.ShouldContain("mailto:ik@acme.test"),
            c => c.ShouldContain("STATUS:CONFIRMED"));
    }

    [Fact]
    public void ACancellation_IsACancel_WithTheRaisedSequence()
    {
        var ics = Build(Event with { Sequence = 2, Cancelled = true });

        ics.ShouldSatisfyAllConditions(
            c => c.ShouldContain("METHOD:CANCEL"),
            c => c.ShouldContain("SEQUENCE:2"),
            c => c.ShouldContain("STATUS:CANCELLED"));
    }
}
