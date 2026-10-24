using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Leave;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>Leave reports with their Excel files, and the leave documents as PDF (PLAN L6, D46).</summary>
internal static class LeaveReportEndpoints
{
    public static void MapLeaveReportEndpoints(this IEndpointRouteBuilder group)
    {
        var reports = group.MapGroup("/leave/reports");

        reports.MapGet("/balances", BalancesAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("GetLeaveBalanceReport")
            .WithSummary("Balances of the employments in scope as of a day: earned, used and left")
            .ProducesValidationProblem();
        reports.MapGet("/balances/export", ExportBalancesAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("ExportLeaveBalanceReport")
            .WithSummary("The balance report as Excel, with its filters, sort and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/usage", UsageAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("GetLeaveUsageReport")
            .WithSummary("Approved leaves starting in a period, by person and type: their count and days")
            .ProducesValidationProblem();
        reports.MapGet("/usage/export", ExportUsageAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("ExportLeaveUsageReport")
            .WithSummary("The usage report as Excel, with its filters, sort and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/upcoming", UpcomingAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("GetUpcomingLeavesReport")
            .WithSummary("Leaves starting in the coming days")
            .ProducesValidationProblem();
        reports.MapGet("/upcoming/export", ExportUpcomingAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("ExportUpcomingLeavesReport")
            .WithSummary("The upcoming leaves as Excel, with their filters, sort and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/long-time-no-leave", LongTimeNoLeaveAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("GetLongTimeNoLeaveReport")
            .WithSummary("People who have not taken leave from a balance for some months, with their balance now")
            .ProducesValidationProblem();
        reports.MapGet("/long-time-no-leave/export", ExportLongTimeNoLeaveAsync)
            .RequirePermission(HrPermissions.Leave.Reports.Read)
            .WithName("ExportLongTimeNoLeaveReport")
            .WithSummary("The people without leave as Excel, with their filters, sort and chosen columns")
            .ProducesValidationProblem();

        group.MapGet("/employees/{employeeId:guid}/employments/{employmentId:guid}/leave-record", async (Guid employeeId, Guid employmentId, HttpContext context, ISender sender, CancellationToken ct) =>
                Pdf(context, await sender.Send(new GetLeaveRecordDocumentQuery(employeeId, employmentId), ct)))
            .RequirePermission(HrPermissions.Leave.Balances.Read)
            .WithName("GetLeaveRecordDocument")
            .WithSummary("The annual leave record of an employment as PDF: seniority, days earned, leaves taken and signatures")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/leave-requests/{id:guid}/form", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Pdf(context, await sender.Send(new GetLeaveRequestFormQuery(id), ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Read)
            .WithName("GetLeaveRequestForm")
            .WithSummary("A leave request form as PDF, to be signed")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/me/leave-requests/{id:guid}/form", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Pdf(context, await sender.Send(new GetMyLeaveRequestFormQuery(id), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRead)
            .WithName("GetMyLeaveRequestForm")
            .WithSummary("The form of one of the user's leave requests as PDF")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult Pdf(HttpContext context, Result<HrFileDownload> result) =>
        result.IsSuccess
            ? FileTransfer.Content(context, result.Value.FileName, result.Value.ContentType, result.Value.Length, result.Value.Content)
            : result.Error.ToProblem();

    private static Results<Ok<T>, ProblemHttpResult> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    /// <param name="asOf">The day the balances are computed for.</param>
    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">employeeName, registryNumber, earned, used or balance; a leading - sorts descending.</param>
    /// <param name="companyId">Only this company.</param>
    /// <param name="leaveTypeCode">Only this balance.</param>
    /// <param name="search">Part of the name or of the registry number.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<LeaveBalanceReportRow>>, ProblemHttpResult>> BalancesAsync(
        DateOnly asOf,
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        string? leaveTypeCode,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetLeaveBalanceReportQuery(new PageRequest(page, pageSize, sort), asOf, companyId, leaveTypeCode, search), cancellationToken));

    /// <param name="asOf">The report's day.</param>
    /// <param name="columns">Column keys, comma separated; every column when empty.</param>
    /// <param name="sort">The report's sort.</param>
    /// <param name="companyId">The report's filter.</param>
    /// <param name="leaveTypeCode">The report's filter.</param>
    /// <param name="search">The report's filter.</param>
    /// <param name="exporter">Writes the file.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportBalancesAsync(
        DateOnly asOf,
        string? columns,
        string? sort,
        Guid? companyId,
        string? leaveTypeCode,
        string? search,
        IListExporter exporter,
        CancellationToken cancellationToken) =>
        (await exporter.ExportAsync(
            new ExportList<LeaveBalanceReportRow>(
                "web.hr.leave_reports.balances.title",
                typeof(LeaveAccount),
                LeaveReportExports.Balances,
                paging => new GetLeaveBalanceReportQuery(paging, asOf, companyId, leaveTypeCode, search),
                sort,
                ExportRules.ParseColumns(columns),
                new Dictionary<string, object?> { ["asOf"] = asOf, ["companyId"] = companyId, ["leaveTypeCode"] = leaveTypeCode, ["search"] = search }),
            cancellationToken)).ToFile();

    /// <param name="from">First day of the period.</param>
    /// <param name="to">Last day of the period.</param>
    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">days, requests or leaveTypeCode; a leading - sorts descending.</param>
    /// <param name="companyId">Only this company.</param>
    /// <param name="leaveTypeCode">Only this type.</param>
    /// <param name="search">Part of the name.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<LeaveUsageReportRow>>, ProblemHttpResult>> UsageAsync(
        DateOnly from,
        DateOnly to,
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        string? leaveTypeCode,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetLeaveUsageReportQuery(new PageRequest(page, pageSize, sort), from, to, companyId, leaveTypeCode, search), cancellationToken));

    /// <param name="from">The report's period.</param>
    /// <param name="to">The report's period.</param>
    /// <param name="columns">Column keys, comma separated; every column when empty.</param>
    /// <param name="sort">The report's sort.</param>
    /// <param name="companyId">The report's filter.</param>
    /// <param name="leaveTypeCode">The report's filter.</param>
    /// <param name="search">The report's filter.</param>
    /// <param name="exporter">Writes the file.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportUsageAsync(
        DateOnly from,
        DateOnly to,
        string? columns,
        string? sort,
        Guid? companyId,
        string? leaveTypeCode,
        string? search,
        IListExporter exporter,
        CancellationToken cancellationToken) =>
        (await exporter.ExportAsync(
            new ExportList<LeaveUsageReportRow>(
                "web.hr.leave_reports.usage.title",
                typeof(LeaveRequest),
                LeaveReportExports.Usage,
                paging => new GetLeaveUsageReportQuery(paging, from, to, companyId, leaveTypeCode, search),
                sort,
                ExportRules.ParseColumns(columns),
                new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["companyId"] = companyId, ["leaveTypeCode"] = leaveTypeCode, ["search"] = search }),
            cancellationToken)).ToFile();

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">startDate or number; a leading - sorts descending.</param>
    /// <param name="from">First day; today when empty.</param>
    /// <param name="days">Days ahead, 30 by default.</param>
    /// <param name="companyId">Only this company.</param>
    /// <param name="leaveTypeCode">Only this type.</param>
    /// <param name="includePending">Also the requests waiting for approval.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<LeaveRequestListItem>>, ProblemHttpResult>> UpcomingAsync(
        int? page,
        int? pageSize,
        string? sort,
        DateOnly? from,
        int? days,
        Guid? companyId,
        string? leaveTypeCode,
        bool? includePending,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(
            new GetUpcomingLeavesReportQuery(new PageRequest(page, pageSize, sort), from, days ?? 30, companyId, leaveTypeCode, includePending ?? false),
            cancellationToken));

    /// <param name="columns">Column keys, comma separated; every column when empty.</param>
    /// <param name="sort">The report's sort.</param>
    /// <param name="from">The report's first day.</param>
    /// <param name="days">The report's days ahead.</param>
    /// <param name="companyId">The report's filter.</param>
    /// <param name="leaveTypeCode">The report's filter.</param>
    /// <param name="includePending">The report's filter.</param>
    /// <param name="exporter">Writes the file.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportUpcomingAsync(
        string? columns,
        string? sort,
        DateOnly? from,
        int? days,
        Guid? companyId,
        string? leaveTypeCode,
        bool? includePending,
        IListExporter exporter,
        CancellationToken cancellationToken) =>
        (await exporter.ExportAsync(
            new ExportList<LeaveRequestListItem>(
                "web.hr.leave_reports.upcoming.title",
                typeof(LeaveRequest),
                LeaveReportExports.Upcoming,
                paging => new GetUpcomingLeavesReportQuery(paging, from, days ?? 30, companyId, leaveTypeCode, includePending ?? false),
                sort,
                ExportRules.ParseColumns(columns),
                new Dictionary<string, object?>
                {
                    ["from"] = from,
                    ["days"] = days,
                    ["companyId"] = companyId,
                    ["leaveTypeCode"] = leaveTypeCode,
                    ["includePending"] = includePending,
                }),
            cancellationToken)).ToFile();

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">lastLeaveEnd, employeeName, hireDate or balance; a leading - sorts descending.</param>
    /// <param name="months">Months without leave, 12 by default.</param>
    /// <param name="companyId">Only this company.</param>
    /// <param name="search">Part of the name or of the registry number.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<LongTimeNoLeaveRow>>, ProblemHttpResult>> LongTimeNoLeaveAsync(
        int? page,
        int? pageSize,
        string? sort,
        int? months,
        Guid? companyId,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetLongTimeNoLeaveReportQuery(new PageRequest(page, pageSize, sort), months ?? 12, companyId, search), cancellationToken));

    /// <param name="columns">Column keys, comma separated; every column when empty.</param>
    /// <param name="sort">The report's sort.</param>
    /// <param name="months">The report's months.</param>
    /// <param name="companyId">The report's filter.</param>
    /// <param name="search">The report's filter.</param>
    /// <param name="exporter">Writes the file.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportLongTimeNoLeaveAsync(
        string? columns,
        string? sort,
        int? months,
        Guid? companyId,
        string? search,
        IListExporter exporter,
        CancellationToken cancellationToken) =>
        (await exporter.ExportAsync(
            new ExportList<LongTimeNoLeaveRow>(
                "web.hr.leave_reports.long_time_no_leave.title",
                typeof(LeaveRequest),
                LeaveReportExports.LongTimeNoLeave,
                paging => new GetLongTimeNoLeaveReportQuery(paging, months ?? 12, companyId, search),
                sort,
                ExportRules.ParseColumns(columns),
                new Dictionary<string, object?> { ["months"] = months, ["companyId"] = companyId, ["search"] = search }),
            cancellationToken)).ToFile();
}
