using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Domain.Calendars;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Calendars;

// Public holidays and work schedules (PLAN P9, D33). Reading needs platform.calendars.read; changing needs
// platform.calendars.manage with the Tenant scope (calendars are shared by every company).

public static class CalendarRequests
{
    public const string UnknownScheduleCode = "platform.calendars.errors.unknown_schedule";
    public const string UnknownCalendarCode = "platform.calendars.errors.unknown_calendar";
    public const int NameMaxLength = 100;

    internal static void AddNameRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : INamedInput
    {
        validator.RuleFor(c => c.Name).MustCoverDefaultCulture(localization);
        validator.RuleFor(c => c.Name).Custom((value, context) =>
        {
            var result = LocalizedString.Create(value);
            if (result.IsSuccess && result.Value.Values.Values.Any(text => text.Length > NameMaxLength))
            {
                context.AddFailure(new FluentValidation.Results.ValidationFailure("name", "common.errors.max_length")
                {
                    ErrorCode = "common.errors.max_length",
                    FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["MaxLength"] = NameMaxLength },
                });
            }
        });
    }

    internal static async Task<Result> RequireTenantScopeAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken) =>
        await TenantAdministration.IsAllowedAsync(scopes, permission, cancellationToken) ? Result.Success() : CalendarErrors.TenantScopeRequired;
}

/// <summary>A name with a text per culture; the default culture is required.</summary>
public interface INamedInput
{
    IReadOnlyDictionary<string, string?>? Name { get; }
}

// ---- Public holidays ----

public sealed record HolidayCalendarItem(Guid Id, string Code, string Name);

public sealed record GetHolidayCalendarsQuery : IQuery<IReadOnlyList<HolidayCalendarItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Read;
}

/// <remarks>Name holds the text per culture, DisplayName the one in the caller's language.</remarks>
public sealed record HolidayItem(
    Guid Id,
    DateOnly Date,
    IReadOnlyDictionary<string, string> Name,
    string DisplayName,
    HolidayDuration Duration,
    TimeOnly? StartsAt,
    bool IsMovable);

/// <summary>A year of holidays; HasMovableHolidays is false until the year's religious holidays are entered.</summary>
public sealed record HolidayYear(Guid CalendarId, int Year, IReadOnlyList<HolidayItem> Items, bool HasMovableHolidays);

public sealed record GetHolidaysQuery(Guid CalendarId, int Year) : IQuery<HolidayYear>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Read;
}

/// <summary>A holiday as entered. Half days start at StartsAt (13:00 when empty).</summary>
public interface IHolidayFields : INamedInput
{
    DateOnly? Date { get; }

    HolidayDuration? Duration { get; }

    TimeOnly? StartsAt { get; }

    bool IsMovable { get; }
}

public sealed record AddHolidayCommand(
    DateOnly? Date,
    IReadOnlyDictionary<string, string?>? Name,
    HolidayDuration? Duration,
    TimeOnly? StartsAt,
    bool IsMovable) : ICommand<Guid>, IRequirePermission, IHolidayFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid CalendarId { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Manage;
}

public sealed record UpdateHolidayCommand(
    DateOnly? Date,
    IReadOnlyDictionary<string, string?>? Name,
    HolidayDuration? Duration,
    TimeOnly? StartsAt,
    bool IsMovable) : ICommand, IRequirePermission, IHolidayFields
{
    [JsonIgnore]
    public Guid CalendarId { get; init; }

    [JsonIgnore]
    public Guid HolidayId { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Manage;
}

public sealed record RemoveHolidayCommand(Guid CalendarId, Guid HolidayId) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Manage;
}

public sealed record CopiedHolidays(int Count);

/// <summary>Copies the fixed holidays of the previous year to <paramref name="Year"/>; religious ones are entered by hand.</summary>
public sealed record CopyFixedHolidaysCommand(Guid CalendarId, int Year) : ICommand<CopiedHolidays>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Manage;
}

internal sealed class HolidayFieldsValidator<T> : AbstractValidator<T>
    where T : IHolidayFields
{
    public HolidayFieldsValidator(ILocalizationSource localization)
    {
        RuleFor(h => h.Date).NotNull();
        RuleFor(h => h.Duration).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        CalendarRequests.AddNameRules(this, localization);
    }
}

internal sealed class AddHolidayCommandValidator(ILocalizationSource localization) : HolidayFieldsValidatorBase<AddHolidayCommand>(localization);

internal sealed class UpdateHolidayCommandValidator(ILocalizationSource localization) : HolidayFieldsValidatorBase<UpdateHolidayCommand>(localization);

internal abstract class HolidayFieldsValidatorBase<T> : AbstractValidator<T>
    where T : IHolidayFields
{
    protected HolidayFieldsValidatorBase(ILocalizationSource localization) => Include(new HolidayFieldsValidator<T>(localization));
}

internal sealed class CopyFixedHolidaysCommandValidator : AbstractValidator<CopyFixedHolidaysCommand>
{
    public CopyFixedHolidaysCommandValidator() => RuleFor(c => c.Year).InclusiveBetween(2000, 2100);
}

internal static class HolidayRules
{
    public static HolidayDefinition ToDefinition(IHolidayFields fields) =>
        new(fields.Date!.Value, LocalizedString.Create(fields.Name).Value, fields.Duration!.Value, fields.StartsAt, fields.IsMovable);

    public static Task<HolidayCalendar?> FindAsync(IPlatformDbContext db, Guid calendarId, CancellationToken cancellationToken) =>
        db.HolidayCalendars.Include(c => c.Holidays).SingleOrDefaultAsync(c => c.Id == HolidayCalendarId.From(calendarId), cancellationToken);
}

internal sealed class GetHolidayCalendarsQueryHandler(IPlatformDbContext db, ILocalizationSource localization)
    : IQueryHandler<GetHolidayCalendarsQuery, IReadOnlyList<HolidayCalendarItem>>
{
    public async Task<Result<IReadOnlyList<HolidayCalendarItem>>> Handle(GetHolidayCalendarsQuery request, CancellationToken cancellationToken)
    {
        var culture = await CalendarTexts.CultureAsync(localization, cancellationToken);
        var calendars = await db.HolidayCalendars.AsNoTracking().OrderBy(c => c.Code).ToListAsync(cancellationToken);
        IReadOnlyList<HolidayCalendarItem> items = [.. calendars.Select(c => new HolidayCalendarItem(c.Id.Value, c.Code, culture.Pick(c.Name)))];
        return Result.Success(items);
    }
}

internal sealed class GetHolidaysQueryHandler(IPlatformDbContext db, ILocalizationSource localization) : IQueryHandler<GetHolidaysQuery, HolidayYear>
{
    public async Task<Result<HolidayYear>> Handle(GetHolidaysQuery request, CancellationToken cancellationToken)
    {
        var calendar = await HolidayRules.FindAsync(db, request.CalendarId, cancellationToken);
        if (calendar is null)
        {
            return CommonErrors.NotFound;
        }

        var culture = await CalendarTexts.CultureAsync(localization, cancellationToken);
        var items = calendar.Holidays
            .Where(h => h.Date.Year == request.Year)
            .OrderBy(h => h.Date)
            .Select(h => new HolidayItem(h.Id.Value, h.Date, h.Name.Values, culture.Pick(h.Name), h.Duration, h.StartsAt, h.IsMovable))
            .ToList();
        return new HolidayYear(calendar.Id.Value, request.Year, items, calendar.HasMovableHolidays(request.Year));
    }
}

internal sealed class AddHolidayCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICalendarCache cache, IAfterCommit afterCommit)
    : ICommandHandler<AddHolidayCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddHolidayCommand request, CancellationToken cancellationToken)
    {
        var allowed = await CalendarRequests.RequireTenantScopeAsync(scopes, request.Permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var calendar = await HolidayRules.FindAsync(db, request.CalendarId, cancellationToken);
        if (calendar is null)
        {
            return CommonErrors.NotFound;
        }

        var holiday = calendar.AddHoliday(HolidayRules.ToDefinition(request));
        if (holiday.IsFailure)
        {
            return holiday.Error;
        }

        afterCommit.Enqueue(cache.InvalidateAsync);
        return holiday.Value.Id.Value;
    }
}

internal sealed class UpdateHolidayCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICalendarCache cache, IAfterCommit afterCommit)
    : ICommandHandler<UpdateHolidayCommand>
{
    public async Task<Result> Handle(UpdateHolidayCommand request, CancellationToken cancellationToken)
    {
        var allowed = await CalendarRequests.RequireTenantScopeAsync(scopes, request.Permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var calendar = await HolidayRules.FindAsync(db, request.CalendarId, cancellationToken);
        if (calendar is null)
        {
            return CommonErrors.NotFound;
        }

        var updated = calendar.UpdateHoliday(HolidayId.From(request.HolidayId), HolidayRules.ToDefinition(request));
        if (updated.IsSuccess)
        {
            afterCommit.Enqueue(cache.InvalidateAsync);
        }

        return updated;
    }
}

internal sealed class RemoveHolidayCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICalendarCache cache, IAfterCommit afterCommit)
    : ICommandHandler<RemoveHolidayCommand>
{
    public async Task<Result> Handle(RemoveHolidayCommand request, CancellationToken cancellationToken)
    {
        var allowed = await CalendarRequests.RequireTenantScopeAsync(scopes, request.Permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var calendar = await HolidayRules.FindAsync(db, request.CalendarId, cancellationToken);
        if (calendar is null)
        {
            return CommonErrors.NotFound;
        }

        var removed = calendar.RemoveHoliday(HolidayId.From(request.HolidayId));
        if (removed.IsSuccess)
        {
            afterCommit.Enqueue(cache.InvalidateAsync);
        }

        return removed;
    }
}

internal sealed class CopyFixedHolidaysCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICalendarCache cache, IAfterCommit afterCommit)
    : ICommandHandler<CopyFixedHolidaysCommand, CopiedHolidays>
{
    public async Task<Result<CopiedHolidays>> Handle(CopyFixedHolidaysCommand request, CancellationToken cancellationToken)
    {
        var allowed = await CalendarRequests.RequireTenantScopeAsync(scopes, request.Permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var calendar = await HolidayRules.FindAsync(db, request.CalendarId, cancellationToken);
        if (calendar is null)
        {
            return CommonErrors.NotFound;
        }

        var count = calendar.CopyFixedHolidays(request.Year - 1, request.Year);
        afterCommit.Enqueue(cache.InvalidateAsync);
        return new CopiedHolidays(count);
    }
}

// ---- Work schedules ----

/// <summary>One day of the week; a list always holds Monday to Sunday in order.</summary>
public sealed record WorkDayDto(bool IsWorkingDay, TimeOnly? Start, TimeOnly? End, TimeOnly? BreakStart, TimeOnly? BreakEnd)
{
    internal static IReadOnlyList<WorkDayDto> From(IReadOnlyList<WorkDay> days) =>
        [.. days.Select(d => new WorkDayDto(d.IsWorkingDay, d.Start, d.End, d.BreakStart, d.BreakEnd))];

    internal static IReadOnlyList<WorkDay> ToDomain(IReadOnlyList<WorkDayDto>? days) =>
        days is null ? [] : [.. days.Select((d, i) => new WorkDay((DayOfWeek)((i + 1) % 7), d.IsWorkingDay, d.Start, d.End, d.BreakStart, d.BreakEnd))];
}

/// <remarks>Send Version back when updating (optimistic concurrency).</remarks>
public sealed record WorkScheduleItem(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string> Name,
    string DisplayName,
    Guid? CompanyId,
    string? CompanyName,
    Guid HolidayCalendarId,
    IReadOnlyList<WorkDayDto> Days,
    int WorkingDaysPerWeek,
    decimal WeeklyHours,
    bool IsDefault,
    bool IsActive,
    uint Version);

/// <summary>The tenant's schedules; with CompanyId, the company's own and the shared ones.</summary>
public sealed record GetWorkSchedulesQuery(Guid? CompanyId) : IQuery<IReadOnlyList<WorkScheduleItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Read;
}

public interface IWorkScheduleFields : INamedInput
{

    Guid HolidayCalendarId { get; }

    IReadOnlyList<WorkDayDto>? Days { get; }

    bool IsDefault { get; }
}

/// <summary>Adds a schedule for a company, or for every company when CompanyId is null.</summary>
public sealed record CreateWorkScheduleCommand(
    string? Code,
    IReadOnlyDictionary<string, string?>? Name,
    Guid? CompanyId,
    Guid HolidayCalendarId,
    IReadOnlyList<WorkDayDto>? Days,
    bool IsDefault) : ICommand<Guid>, IRequirePermission, IWorkScheduleFields
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Manage;
}

/// <summary>Changes a schedule; the code and the company never change. The default cannot be deactivated.</summary>
public sealed record UpdateWorkScheduleCommand(
    uint Version,
    IReadOnlyDictionary<string, string?>? Name,
    Guid HolidayCalendarId,
    IReadOnlyList<WorkDayDto>? Days,
    bool IsDefault,
    bool IsActive) : ICommand, IRequirePermission, IWorkScheduleFields
{
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Manage;
}

/// <summary>Days of a schedule with holidays and working hours, for screens and checks.</summary>
public sealed record GetWorkingDaysQuery(Guid ScheduleId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<WorkingDay>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Calendars.Read;
}

internal sealed class CreateWorkScheduleCommandValidator : AbstractValidator<CreateWorkScheduleCommand>
{
    public CreateWorkScheduleCommandValidator(ILocalizationSource localization)
    {
        RuleFor(c => c.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, WorkSchedule.CodeMaxLength));
        RuleFor(c => c.Days).NotNull();
        CalendarRequests.AddNameRules(this, localization);
    }
}

internal sealed class UpdateWorkScheduleCommandValidator : AbstractValidator<UpdateWorkScheduleCommand>
{
    public UpdateWorkScheduleCommandValidator(ILocalizationSource localization)
    {
        RuleFor(c => c.Days).NotNull();
        CalendarRequests.AddNameRules(this, localization);
    }
}

internal sealed class GetWorkingDaysQueryValidator : AbstractValidator<GetWorkingDaysQuery>
{
    public GetWorkingDaysQueryValidator()
    {
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From);
        RuleFor(q => q.To).Must((q, to) => to.DayNumber - q.From.DayNumber < WorkingDays.MaxDays)
            .WithErrorCode("common.errors.less_than")
            .When(q => q.To >= q.From);
    }
}

internal sealed class GetWorkSchedulesQueryHandler(IPlatformDbContext db, ILocalizationSource localization)
    : IQueryHandler<GetWorkSchedulesQuery, IReadOnlyList<WorkScheduleItem>>
{
    public async Task<Result<IReadOnlyList<WorkScheduleItem>>> Handle(GetWorkSchedulesQuery request, CancellationToken cancellationToken)
    {
        var schedules = db.WorkSchedules.AsNoTracking();
        if (request.CompanyId is { } companyId)
        {
            schedules = schedules.Where(s => s.CompanyId == null || s.CompanyId == CompanyId.From(companyId));
        }

        var rows = await schedules
            .Select(s => new { Schedule = s, Version = EF.Property<uint>(s, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        var companyIds = rows.Select(r => r.Schedule.CompanyId).OfType<CompanyId>().Distinct().ToList();
        var companies = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.ShortName, cancellationToken);
        var culture = await CalendarTexts.CultureAsync(localization, cancellationToken);

        IReadOnlyList<WorkScheduleItem> items =
        [
            .. rows
                .Select(r => r.Schedule switch
                {
                    var s => new WorkScheduleItem(
                        s.Id.Value,
                        s.Code,
                        s.Name.Values,
                        culture.Pick(s.Name),
                        s.CompanyId?.Value,
                        s.CompanyId is { } id ? companies.GetValueOrDefault(id) : null,
                        s.HolidayCalendarId.Value,
                        WorkDayDto.From(s.Days),
                        s.WorkingDaysPerWeek,
                        s.Days.Sum(d => d.Hours()),
                        s.IsDefault,
                        s.IsActive,
                        r.Version),
                })
                .OrderBy(s => s.CompanyName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(s => s.Code, StringComparer.Ordinal),
        ];
        return Result.Success(items);
    }
}

internal sealed class CreateWorkScheduleCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICalendarCache cache, IAfterCommit afterCommit)
    : ICommandHandler<CreateWorkScheduleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateWorkScheduleCommand request, CancellationToken cancellationToken)
    {
        var allowed = await CalendarRequests.RequireTenantScopeAsync(scopes, request.Permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var code = Code.Create(request.Code, WorkSchedule.CodeMaxLength).Value;
        var errors = new List<FieldError>();
        if (await db.WorkSchedules.AnyAsync(s => s.Code == code.Value, cancellationToken))
        {
            errors.Add(new FieldError("code", "common.errors.duplicate"));
        }

        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            errors.Add(new FieldError("companyId", CommonErrors.NotFound.Code));
        }

        if (!await db.HolidayCalendars.AnyAsync(c => c.Id == HolidayCalendarId.From(request.HolidayCalendarId), cancellationToken))
        {
            errors.Add(new FieldError("holidayCalendarId", CalendarRequests.UnknownCalendarCode));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var schedule = WorkSchedule.Create(
            code,
            LocalizedString.Create(request.Name).Value,
            request.CompanyId is { } id ? CompanyId.From(id) : null,
            HolidayCalendarId.From(request.HolidayCalendarId),
            WorkDayDto.ToDomain(request.Days));
        if (schedule.IsFailure)
        {
            return schedule.Error;
        }

        if (request.IsDefault)
        {
            schedule.Value.MakeDefault();
        }

        db.WorkSchedules.Add(schedule.Value);
        afterCommit.Enqueue(cache.InvalidateAsync);
        return schedule.Value.Id.Value;
    }
}

internal sealed class UpdateWorkScheduleCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICalendarCache cache, IAfterCommit afterCommit)
    : ICommandHandler<UpdateWorkScheduleCommand>
{
    public async Task<Result> Handle(UpdateWorkScheduleCommand request, CancellationToken cancellationToken)
    {
        var allowed = await CalendarRequests.RequireTenantScopeAsync(scopes, request.Permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var schedule = await db.WorkSchedules.SingleOrDefaultAsync(s => s.Id == WorkScheduleId.From(request.Id), cancellationToken);
        if (schedule is null)
        {
            return CommonErrors.NotFound;
        }

        if (!await db.HolidayCalendars.AnyAsync(c => c.Id == HolidayCalendarId.From(request.HolidayCalendarId), cancellationToken))
        {
            return Error.Validation([new FieldError("holidayCalendarId", CalendarRequests.UnknownCalendarCode)]);
        }

        db.Entry(schedule).ExpectVersion(request.Version);
        var updated = schedule.Update(LocalizedString.Create(request.Name).Value, HolidayCalendarId.From(request.HolidayCalendarId), WorkDayDto.ToDomain(request.Days));
        if (updated.IsFailure)
        {
            return updated;
        }

        if (request.IsDefault)
        {
            schedule.MakeDefault();
        }

        if (request.IsActive)
        {
            schedule.Activate();
        }
        else
        {
            var deactivated = schedule.Deactivate();
            if (deactivated.IsFailure)
            {
                return deactivated;
            }
        }

        afterCommit.Enqueue(cache.InvalidateAsync);
        return Result.Success();
    }
}

/// <summary>Keeps one default per company and one for the tenant (rule 7: the other schedules change by event).</summary>
internal sealed class WorkScheduleMadeDefaultHandler(IPlatformDbContext db) : IDomainEventHandler<WorkScheduleMadeDefault>
{
    public async Task Handle(WorkScheduleMadeDefault domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var others = await db.WorkSchedules
            .Where(s => s.IsDefault && s.Id != domainEvent.ScheduleId && s.CompanyId == domainEvent.CompanyId)
            .ToListAsync(cancellationToken);
        others.ForEach(s => s.ClearDefault());
    }
}

internal sealed class GetWorkingDaysQueryHandler(IWorkingCalendar calendar) : IQueryHandler<GetWorkingDaysQuery, IReadOnlyList<WorkingDay>>
{
    public async Task<Result<IReadOnlyList<WorkingDay>>> Handle(GetWorkingDaysQuery request, CancellationToken cancellationToken)
    {
        if (await calendar.GetDayAsync(request.ScheduleId, request.From, cancellationToken) is null)
        {
            return CommonErrors.NotFound;
        }

        return Result.Success(await calendar.GetDaysAsync(request.ScheduleId, request.From, request.To, cancellationToken));
    }
}

/// <summary>Names of calendars and holidays in the caller's language, falling back to the tenant's default.</summary>
internal sealed record CalendarTexts(string Culture, string DefaultCulture)
{
    public static async Task<CalendarTexts> CultureAsync(ILocalizationSource localization, CancellationToken cancellationToken)
    {
        var snapshot = await localization.GetAsync(cancellationToken);
        var culture = snapshot.Match(System.Globalization.CultureInfo.CurrentUICulture.Name) ?? snapshot.DefaultCulture;
        return new CalendarTexts(culture, snapshot.DefaultCulture);
    }

    public string Pick(LocalizedString text) => text.Get(Culture, DefaultCulture);
}
