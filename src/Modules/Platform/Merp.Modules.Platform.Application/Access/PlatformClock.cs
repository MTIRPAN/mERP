namespace Merp.Modules.Platform.Application.Access;

/// <summary>
/// Calendar day of the tenant for validity dates of role assignments. Assignments are tenant wide, so the default
/// time zone applies (D10), not the time zone of a company.
/// </summary>
public static class PlatformClock
{
    public const string DefaultTimeZoneId = "Europe/Istanbul";

    private static readonly TimeZoneInfo DefaultTimeZone = TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);

    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), DefaultTimeZone).DateTime);
    }

    /// <summary>When the current day of the tenant ends, as an absolute time.</summary>
    public static DateTimeOffset EndOfToday(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), DefaultTimeZone);
        var midnight = local.Date.AddDays(1);
        return new DateTimeOffset(midnight, DefaultTimeZone.GetUtcOffset(midnight));
    }
}
