using System.Globalization;

namespace Merp.Modules.HR.Domain.Leave;

/// <summary>An entitlement due on a day: the period it belongs to and its days.</summary>
public sealed record AccrualEvent(DateOnly Date, string PeriodKey, decimal Days);

/// <summary>
/// When the annual leave is earned and how much (docs/domain/izin-kurallari.md §2, §5, D43). The rules of the policy
/// version in force on each day apply.
/// </summary>
public static class LeaveAccruals
{
    private const int MaxYears = 80;

    /// <summary>
    /// The entitlements due after <paramref name="after"/> up to <paramref name="until"/>, both days, for service from
    /// <paramref name="seniority"/>:
    /// <list type="bullet">
    /// <item>AnniversaryLumpSum: on each anniversary, the days for the years completed and the age that day.</item>
    /// <item>MonthlyAccrual: on each monthly day of the seniority date, a twelfth of the days of the service year in progress.</item>
    /// <item>CalendarYearLumpSum: on each 1 January, the days for the years completed and the age that day.</item>
    /// </list>
    /// </summary>
    /// <param name="rulesOn">The policy's rules on a day; null before the policy's first version.</param>
    /// <param name="seniority">Start of service.</param>
    /// <param name="birthDate">For the age minimums; unknown skips them.</param>
    /// <param name="after">Entitlements on this day or before are not due (already handled, or before accruals started).</param>
    /// <param name="until">The last day looked at, today for the accrual job.</param>
    /// <param name="matches">Whether an extra day rule's criterion matches the employee on a day.</param>
    public static IReadOnlyList<AccrualEvent> Due(
        Func<DateOnly, AnnualLeaveRules?> rulesOn,
        DateOnly seniority,
        DateOnly? birthDate,
        DateOnly after,
        DateOnly until,
        Func<DateOnly, PolicyCriterion, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(rulesOn);
        ArgumentNullException.ThrowIfNull(matches);
        var due = new List<AccrualEvent>();
        if (until <= after)
        {
            return due;
        }

        foreach (var date in Candidates(rulesOn, seniority, after, until))
        {
            var rules = rulesOn(date);
            if (rules is null)
            {
                continue;
            }

            int? age = birthDate is { } birth ? CompletedYears(birth, date) : null;
            var completed = CompletedYears(seniority, date);
            var days = rules.AccrualMethod switch
            {
                AccrualMethod.MonthlyAccrual => rules.Entitlement(CompletedYears(seniority, date.AddDays(-1)) + 1, age, c => matches(date, c)) / 12,
                _ => rules.Entitlement(completed, age, c => matches(date, c)),
            };
            days = Math.Round(days, 2, MidpointRounding.AwayFromZero);
            if (days > 0)
            {
                due.Add(new AccrualEvent(date, Key(rules.AccrualMethod, date), days));
            }
        }

        return due;
    }

    /// <summary>The next entitlement after <paramref name="after"/>, within a year and a month.</summary>
    public static AccrualEvent? Next(
        Func<DateOnly, AnnualLeaveRules?> rulesOn,
        DateOnly seniority,
        DateOnly? birthDate,
        DateOnly after,
        Func<DateOnly, PolicyCriterion, bool> matches) =>
        Due(rulesOn, seniority, birthDate, after, after.AddMonths(13), matches) is [var first, ..] ? first : null;

    /// <summary>Whole years from <paramref name="from"/> to <paramref name="day"/>, an anniversary counting on its day.</summary>
    public static int CompletedYears(DateOnly from, DateOnly day)
    {
        var years = day.Year - from.Year;
        return Math.Max(0, from.AddYears(years) > day ? years - 1 : years);
    }

    private static string Key(AccrualMethod method, DateOnly date) => method switch
    {
        AccrualMethod.MonthlyAccrual => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        AccrualMethod.CalendarYearLumpSum => date.ToString("yyyy", CultureInfo.InvariantCulture),
        _ => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>The days an entitlement may fall on in the range, by the method of the rules in force on each.</summary>
    private static SortedSet<DateOnly> Candidates(Func<DateOnly, AnnualLeaveRules?> rulesOn, DateOnly seniority, DateOnly after, DateOnly until)
    {
        var days = new SortedSet<DateOnly>();
        for (var n = 1; n <= MaxYears; n++)
        {
            var anniversary = seniority.AddYears(n);
            if (anniversary > until)
            {
                break;
            }

            if (anniversary > after && rulesOn(anniversary)?.AccrualMethod == AccrualMethod.AnniversaryLumpSum)
            {
                days.Add(anniversary);
            }
        }

        for (var n = 1; n <= MaxYears * 12; n++)
        {
            var monthly = seniority.AddMonths(n);
            if (monthly > until)
            {
                break;
            }

            if (monthly > after && rulesOn(monthly)?.AccrualMethod == AccrualMethod.MonthlyAccrual)
            {
                days.Add(monthly);
            }
        }

        for (var year = seniority.Year + 1; year <= until.Year; year++)
        {
            var newYear = new DateOnly(year, 1, 1);
            if (newYear > after && newYear <= until && rulesOn(newYear)?.AccrualMethod == AccrualMethod.CalendarYearLumpSum)
            {
                days.Add(newYear);
            }
        }

        return days;
    }
}
