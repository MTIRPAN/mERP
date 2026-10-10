using System.Globalization;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Calendars;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Infrastructure.Calendars;

/// <summary>
/// <see cref="IWorkingCalendar"/> over the tenant's schedules and holidays (PLAN P9, D33). Schedules, holidays per
/// year and resolutions are cached for an hour and dropped after any change (<see cref="ICalendarCache"/>).
/// </summary>
internal sealed class PlatformWorkingCalendar(
    PlatformDbContext db,
    HybridCache cache,
    ITenantContext tenant,
    ILocalizationSource localization) : IWorkingCalendar
{
    private static readonly HybridCacheEntryOptions Entry = new() { Expiration = TimeSpan.FromHours(1), LocalCacheExpiration = TimeSpan.FromMinutes(5) };

    public async Task<Guid?> ResolveScheduleIdAsync(Guid companyId, Guid? branchId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.RequiredTenant.Id;
        return await cache.GetOrCreateAsync(
            $"merp:{tenantId}:calendars:resolve:{companyId:N}:{branchId:N}",
            async ct => await ResolveAsync(CompanyId.From(companyId), branchId is { } id ? BranchId.From(id) : null, ct),
            Entry,
            [CalendarCache.Tag(tenantId)],
            cancellationToken);
    }

    public async Task<WorkingDay?> GetDayAsync(Guid scheduleId, DateOnly date, CancellationToken cancellationToken = default) =>
        (await GetDaysAsync(scheduleId, date, date, cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<WorkingDay>> GetDaysAsync(Guid scheduleId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var schedule = await ScheduleAsync(scheduleId, cancellationToken);
        if (schedule is null)
        {
            return [];
        }

        var holidays = new Dictionary<DateOnly, HolidaySnapshot>();
        for (var year = from.Year; year <= to.Year; year++)
        {
            foreach (var holiday in await HolidaysAsync(schedule.HolidayCalendarId, year, cancellationToken))
            {
                holidays[holiday.Date] = holiday;
            }
        }

        var snapshot = await localization.GetAsync(cancellationToken);
        var culture = snapshot.Match(CultureInfo.CurrentUICulture.Name) ?? snapshot.DefaultCulture;
        return WorkingDays.Compute(schedule, holidays, from, to, h =>
            h.Name.GetValueOrDefault(culture) ?? h.Name.GetValueOrDefault(snapshot.DefaultCulture) ?? h.Name.Values.FirstOrDefault() ?? string.Empty);
    }

    private async Task<Guid?> ResolveAsync(CompanyId companyId, BranchId? branchId, CancellationToken cancellationToken)
    {
        if (branchId is { } branch)
        {
            var own = await db.Branches.AsNoTracking()
                .Where(b => b.Id == branch && b.CompanyId == companyId && b.WorkScheduleId != null)
                .Join(db.WorkSchedules.Where(s => s.IsActive), b => b.WorkScheduleId, s => s.Id, (b, s) => (Guid?)s.Id.Value)
                .SingleOrDefaultAsync(cancellationToken);
            if (own is not null)
            {
                return own;
            }
        }

        var defaults = await db.WorkSchedules.AsNoTracking()
            .Where(s => s.IsDefault && s.IsActive && (s.CompanyId == companyId || s.CompanyId == null))
            .Select(s => new { s.Id, s.CompanyId })
            .ToListAsync(cancellationToken);
        var chosen = defaults.FirstOrDefault(s => s.CompanyId is not null) ?? defaults.FirstOrDefault();
        return chosen?.Id.Value;
    }

    private async Task<ScheduleSnapshot?> ScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequiredTenant.Id;
        return await cache.GetOrCreateAsync(
            $"merp:{tenantId}:calendars:schedule:{scheduleId:N}",
            async ct => await db.WorkSchedules.AsNoTracking()
                .Where(s => s.Id == WorkScheduleId.From(scheduleId))
                .Select(s => new ScheduleSnapshot(s.Id.Value, s.HolidayCalendarId.Value, s.Days))
                .SingleOrDefaultAsync(ct),
            Entry,
            [CalendarCache.Tag(tenantId)],
            cancellationToken);
    }

    private async Task<List<HolidaySnapshot>> HolidaysAsync(Guid calendarId, int year, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequiredTenant.Id;
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        return await cache.GetOrCreateAsync(
            $"merp:{tenantId}:calendars:holidays:{calendarId:N}:{year}",
            async ct =>
            {
                var holidays = await db.Set<Holiday>().AsNoTracking()
                    .Where(h => h.CalendarId == HolidayCalendarId.From(calendarId) && h.Date >= from && h.Date <= to)
                    .ToListAsync(ct);
                return holidays.Select(h => new HolidaySnapshot(h.Date, h.Fraction, h.StartsAt, h.Name.Values)).ToList();
            },
            Entry,
            [CalendarCache.Tag(tenantId)],
            cancellationToken);
    }
}

internal sealed class CalendarCache(HybridCache cache, ITenantContext tenant) : ICalendarCache
{
    public static string Tag(Guid tenantId) => $"{tenantId}:calendars";

    public async Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(Tag(tenant.RequiredTenant.Id), cancellationToken);
}

/// <summary>Every 1 October: reminds administrators when next year's religious holidays are missing (PLAN P9).</summary>
public sealed class HolidayReminderJob(ITenantJobRunner tenants, TimeProvider timeProvider) : IRecurringJob
{
    public const string JobId = "calendars.holiday-reminder";

    /// <summary>06:00 UTC on 1 October (09:00 in Türkiye).</summary>
    public const string Cron = "0 6 1 10 *";

    public Task RunAsync(CancellationToken cancellationToken)
    {
        var nextYear = timeProvider.GetUtcNow().Year + 1;
        return tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new RemindMissingHolidaysCommand(nextYear), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"The holiday reminder failed: {result.Error.Code}");
                }
            },
            cancellationToken);
    }
}

/// <summary>
/// Seeds the Turkish public holidays of 2026 and 2027 (izin-kurallari.md §7) and a standard schedule as the tenant's
/// default, once: calendars and schedules the tenant already has are left alone.
/// </summary>
internal sealed partial class CalendarSeeder(ILogger<CalendarSeeder> logger) : ITenantDataSeeder
{
    private const string Culture = "tr-TR";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<PlatformDbContext>();
        if (await db.HolidayCalendars.AnyAsync(cancellationToken))
        {
            return;
        }

        var calendar = HolidayCalendar.Create(Code.Create("TR", HolidayCalendar.CodeMaxLength).Value, LocalizedString.Of(Culture, "Türkiye resmi tatilleri"));
        foreach (var (date, name, half, movable) in TurkishHolidays())
        {
            var added = calendar.AddHoliday(new HolidayDefinition(
                date,
                LocalizedString.Of(Culture, name),
                half ? HolidayDuration.Half : HolidayDuration.Full,
                null,
                movable));
            if (added.IsFailure)
            {
                throw new InvalidOperationException($"The seed holiday of {date} is invalid: {added.Error.Code}.");
            }
        }

        db.HolidayCalendars.Add(calendar);
        if (!await db.WorkSchedules.AnyAsync(cancellationToken))
        {
            db.WorkSchedules.Add(Schedule("STANDART", "Standart (Pzt–Cum)", calendar.Id, saturday: false, isDefault: true));
            db.WorkSchedules.Add(Schedule("ALTIGUN", "Haftada 6 gün (Pzt–Cmt)", calendar.Id, saturday: true, isDefault: false));
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, calendar.Holidays.Count);
    }

    private static WorkSchedule Schedule(string code, string name, HolidayCalendarId calendarId, bool saturday, bool isDefault)
    {
        WorkDay Weekday(DayOfWeek day) => WorkDay.Working(day, new TimeOnly(8, 30), new TimeOnly(18, 0), new TimeOnly(12, 30), new TimeOnly(13, 30));
        IReadOnlyList<WorkDay> days =
        [
            Weekday(DayOfWeek.Monday), Weekday(DayOfWeek.Tuesday), Weekday(DayOfWeek.Wednesday), Weekday(DayOfWeek.Thursday), Weekday(DayOfWeek.Friday),
            saturday ? WorkDay.Working(DayOfWeek.Saturday, new TimeOnly(8, 30), new TimeOnly(13, 30)) : WorkDay.Off(DayOfWeek.Saturday),
            WorkDay.Off(DayOfWeek.Sunday),
        ];
        var schedule = WorkSchedule.Create(Code.Create(code, WorkSchedule.CodeMaxLength).Value, LocalizedString.Of(Culture, name), null, calendarId, days).Value;
        if (isDefault)
        {
            schedule.MakeDefault();
            schedule.ClearDomainEvents();
        }

        return schedule;
    }

    /// <summary>izin-kurallari.md §7: 2429 sayılı Kanun and the Diyanet calendar. Eves and 28 October are half days from 13:00.</summary>
    internal static IEnumerable<(DateOnly Date, string Name, bool Half, bool Movable)> TurkishHolidays()
    {
        foreach (var year in new[] { 2026, 2027 })
        {
            yield return (new DateOnly(year, 1, 1), "Yılbaşı", false, false);
            yield return (new DateOnly(year, 4, 23), "Ulusal Egemenlik ve Çocuk Bayramı", false, false);
            yield return (new DateOnly(year, 5, 1), "Emek ve Dayanışma Günü", false, false);

            // A fixed holiday even when a bayram falls on it (2027), so copying the fixed holidays keeps it
            yield return (new DateOnly(year, 5, 19), year == 2027 ? "Atatürk'ü Anma, Gençlik ve Spor Bayramı; Kurban Bayramı" : "Atatürk'ü Anma, Gençlik ve Spor Bayramı", false, false);
            yield return (new DateOnly(year, 7, 15), "Demokrasi ve Milli Birlik Günü", false, false);
            yield return (new DateOnly(year, 8, 30), "Zafer Bayramı", false, false);
            yield return (new DateOnly(year, 10, 28), "Cumhuriyet Bayramı arifesi", true, false);
            yield return (new DateOnly(year, 10, 29), "Cumhuriyet Bayramı", false, false);
        }

        yield return (new DateOnly(2026, 3, 19), "Ramazan Bayramı arifesi", true, true);
        for (var day = 20; day <= 22; day++)
        {
            yield return (new DateOnly(2026, 3, day), "Ramazan Bayramı", false, true);
        }

        yield return (new DateOnly(2026, 5, 26), "Kurban Bayramı arifesi", true, true);
        for (var day = 27; day <= 30; day++)
        {
            yield return (new DateOnly(2026, 5, day), "Kurban Bayramı", false, true);
        }

        yield return (new DateOnly(2027, 3, 8), "Ramazan Bayramı arifesi", true, true);
        for (var day = 9; day <= 11; day++)
        {
            yield return (new DateOnly(2027, 3, day), "Ramazan Bayramı", false, true);
        }

        yield return (new DateOnly(2027, 5, 15), "Kurban Bayramı arifesi", true, true);
        for (var day = 16; day <= 18; day++)
        {
            yield return (new DateOnly(2027, 5, day), "Kurban Bayramı", false, true);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Holiday calendar seeded with {Count} holidays")]
    private static partial void LogSeeded(ILogger logger, int count);
}
