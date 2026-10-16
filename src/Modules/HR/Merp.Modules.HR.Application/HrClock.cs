namespace Merp.Modules.HR.Application;

/// <summary>Calendar day of a company: day boundaries follow the company's time zone (D10).</summary>
internal static class HrClock
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
