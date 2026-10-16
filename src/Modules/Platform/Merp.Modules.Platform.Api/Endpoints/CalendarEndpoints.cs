using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>Public holidays and work schedules (PLAN P9, D33). Changes need the Tenant scope.</summary>
internal static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this IEndpointRouteBuilder group)
    {
        var calendars = group.MapGroup("/holiday-calendars");

        calendars.MapGet("/", ListCalendarsAsync)
            .RequirePermission(PlatformPermissions.Calendars.Read)
            .WithName("ListHolidayCalendars")
            .WithSummary("Holiday calendars of the tenant");

        calendars.MapGet("/{id:guid}/holidays", ListHolidaysAsync)
            .RequirePermission(PlatformPermissions.Calendars.Read)
            .WithName("ListHolidays")
            .WithSummary("Holidays of a year, and whether its religious holidays are entered")
            .ProducesProblem(StatusCodes.Status404NotFound);

        calendars.MapPost("/{id:guid}/holidays", AddHolidayAsync)
            .RequirePermission(PlatformPermissions.Calendars.Manage)
            .WithName("AddHoliday")
            .WithSummary("Add a holiday; a half day starts at 13:00 unless given")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        calendars.MapPut("/{id:guid}/holidays/{holidayId:guid}", UpdateHolidayAsync)
            .RequirePermission(PlatformPermissions.Calendars.Manage)
            .WithName("UpdateHoliday")
            .WithSummary("Change a holiday")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        calendars.MapDelete("/{id:guid}/holidays/{holidayId:guid}", RemoveHolidayAsync)
            .RequirePermission(PlatformPermissions.Calendars.Manage)
            .WithName("RemoveHoliday")
            .WithSummary("Remove a holiday")
            .ProducesProblem(StatusCodes.Status404NotFound);

        calendars.MapPost("/{id:guid}/copy-fixed-holidays", CopyFixedHolidaysAsync)
            .RequirePermission(PlatformPermissions.Calendars.Manage)
            .WithName("CopyFixedHolidays")
            .WithSummary("Copy the previous year's fixed holidays to a year; religious holidays are entered by hand")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var schedules = group.MapGroup("/work-schedules");

        schedules.MapGet("/", ListSchedulesAsync)
            .RequirePermission(PlatformPermissions.Calendars.Read)
            .WithName("ListWorkSchedules")
            .WithSummary("Work schedules; with companyId, the company's own and the shared ones");

        schedules.MapPost("/", CreateScheduleAsync)
            .RequirePermission(PlatformPermissions.Calendars.Manage)
            .WithName("CreateWorkSchedule")
            .WithSummary("Add a work schedule; days are Monday to Sunday")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        schedules.MapPut("/{id:guid}", UpdateScheduleAsync)
            .RequirePermission(PlatformPermissions.Calendars.Manage)
            .WithName("UpdateWorkSchedule")
            .WithSummary("Change a work schedule; the code and the company never change")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        schedules.MapGet("/{id:guid}/days", ListDaysAsync)
            .RequirePermission(PlatformPermissions.Calendars.Read)
            .WithName("ListWorkingDays")
            .WithSummary("Days of a schedule with holidays and working hours (at most 400 days)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<HolidayCalendarItem>>, ProblemHttpResult>> ListCalendarsAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetHolidayCalendarsQuery(), cancellationToken));

    /// <param name="id">The calendar.</param>
    /// <param name="year">The year.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<HolidayYear>, ProblemHttpResult>> ListHolidaysAsync(Guid id, int year, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetHolidaysQuery(id, year), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> AddHolidayAsync(
        Guid id,
        AddHolidayCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { CalendarId = id }, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/holiday-calendars/{id}/holidays/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateHolidayAsync(
        Guid id,
        Guid holidayId,
        UpdateHolidayCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { CalendarId = id, HolidayId = holidayId }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveHolidayAsync(Guid id, Guid holidayId, ISender sender, CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(new RemoveHolidayCommand(id, holidayId), cancellationToken));

    private static async Task<Results<Ok<CopiedHolidays>, ProblemHttpResult>> CopyFixedHolidaysAsync(
        Guid id,
        CopyFixedHolidaysRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new CopyFixedHolidaysCommand(id, request.Year), cancellationToken));

    /// <param name="companyId">Only the schedules a company may use.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<WorkScheduleItem>>, ProblemHttpResult>> ListSchedulesAsync(Guid? companyId, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetWorkSchedulesQuery(companyId), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateScheduleAsync(
        CreateWorkScheduleCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/work-schedules/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateScheduleAsync(
        Guid id,
        UpdateWorkScheduleCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { Id = id }, cancellationToken));

    /// <param name="id">The schedule.</param>
    /// <param name="from">First day.</param>
    /// <param name="to">Last day, inclusive.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<WorkingDay>>, ProblemHttpResult>> ListDaysAsync(
        Guid id,
        DateOnly from,
        DateOnly to,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetWorkingDaysQuery(id, from, to), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<NoContent, ProblemHttpResult> NoContentOrProblem(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}

/// <summary>The year to fill with the previous year's fixed holidays.</summary>
public sealed record CopyFixedHolidaysRequest(int Year);
