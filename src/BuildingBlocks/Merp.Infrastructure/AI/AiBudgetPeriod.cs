namespace Merp.Infrastructure.AI;

/// <summary>
/// AI budgets are monthly. Months start at midnight in <see cref="TimeZoneId"/> until companies carry their own
/// time zone (PLAN P1).
/// </summary>
public static class AiBudgetPeriod
{
    public const string TimeZoneId = "Europe/Istanbul";

    private static TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>The month <paramref name="now"/> falls in, in the budget time zone.</summary>
    public static (int Year, int Month) MonthOf(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Zone);
        return (local.Year, local.Month);
    }

    /// <summary>First instant of the month, in UTC.</summary>
    public static DateTimeOffset Start(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(start, Zone.GetUtcOffset(start)).ToUniversalTime();
    }

    public static DateTimeOffset CurrentStart(DateTimeOffset now)
    {
        var (year, month) = MonthOf(now);
        return Start(year, month);
    }
}
