namespace Merp.Modules.Recruitment.Application;

/// <summary>Calendar days of a company: its time zone, Europe/Istanbul by default.</summary>
internal static class RecruitmentClock
{
    public const string DefaultTimeZoneId = "Europe/Istanbul";

    public static DateOnly Today(TimeProvider timeProvider, string? timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId ?? DefaultTimeZoneId, out var found)
            ? found
            : TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
    }
}
