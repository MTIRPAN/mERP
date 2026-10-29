namespace Merp.Modules.Recruitment.Application;

/// <summary>Calendar days of a company: its time zone, Europe/Istanbul by default.</summary>
internal static class RecruitmentClock
{
    public const string DefaultTimeZoneId = "Europe/Istanbul";

    public static DateOnly Today(TimeProvider timeProvider, string? timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateOnly.FromDateTime(Local(timeProvider.GetUtcNow(), timeZoneId));
    }

    /// <summary>The wall clock time of a moment in the company's time zone, e.g. for an interview invitation (R5).</summary>
    public static DateTime Local(DateTimeOffset at, string? timeZoneId)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId ?? DefaultTimeZoneId, out var found)
            ? found
            : TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        return TimeZoneInfo.ConvertTime(at, zone).DateTime;
    }
}
