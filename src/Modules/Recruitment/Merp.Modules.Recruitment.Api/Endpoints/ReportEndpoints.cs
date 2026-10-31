using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Reports;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>The recruitment reports and their Excel files (PLAN R9, D56); the rows come in a fixed order.</summary>
internal static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder group)
    {
        var reports = group.MapGroup("/reports");

        reports.MapGet("/summary", async (DateOnly from, DateOnly to, Guid? companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetRecruitmentSummaryQuery(from, to, companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("GetRecruitmentSummary")
            .WithSummary("The headline figures of a period: applications, hires, days to hire and fill, offer acceptance, open requisitions, missing forms")
            .ProducesValidationProblem();

        reports.MapGet("/funnel", async (DateOnly from, DateOnly to, int? page, int? pageSize, Guid? companyId, Guid? openingId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetFunnelReportQuery(new PageRequest(page, pageSize, null), from, to, companyId, openingId), ct)))
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("GetRecruitmentFunnelReport")
            .WithSummary("The funnel of each opening for the applications of a period: stage by stage, reached, conversion and now")
            .ProducesValidationProblem();
        reports.MapGet("/funnel/export", async (DateOnly from, DateOnly to, string? columns, Guid? companyId, Guid? openingId, IListExporter exporter, CancellationToken ct) =>
                (await exporter.ExportAsync(
                    new ExportList<FunnelRow>(
                        "web.recruitment.reports.funnel.title",
                        typeof(JobApplication),
                        RecruitmentReportExports.Funnel,
                        paging => new GetFunnelReportQuery(paging, from, to, companyId, openingId),
                        null,
                        ExportRules.ParseColumns(columns),
                        new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["companyId"] = companyId, ["openingId"] = openingId }),
                    ct)).ToFile())
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("ExportRecruitmentFunnelReport")
            .WithSummary("The funnel report as Excel, with its filters and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/hires", async (DateOnly from, DateOnly to, int? page, int? pageSize, Guid? companyId, Guid? openingId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetHireReportQuery(new PageRequest(page, pageSize, null), from, to, companyId, openingId), ct)))
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("GetRecruitmentHireReport")
            .WithSummary("The hires of a period with the days from the application (time to hire) and from the publication (time to fill)")
            .ProducesValidationProblem();
        reports.MapGet("/hires/export", async (DateOnly from, DateOnly to, string? columns, Guid? companyId, Guid? openingId, IListExporter exporter, CancellationToken ct) =>
                (await exporter.ExportAsync(
                    new ExportList<HireRow>(
                        "web.recruitment.reports.hires.title",
                        typeof(JobApplication),
                        RecruitmentReportExports.Hires,
                        paging => new GetHireReportQuery(paging, from, to, companyId, openingId),
                        null,
                        ExportRules.ParseColumns(columns),
                        new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["companyId"] = companyId, ["openingId"] = openingId }),
                    ct)).ToFile())
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("ExportRecruitmentHireReport")
            .WithSummary("The hire report as Excel, with its filters and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/sources", async (DateOnly from, DateOnly to, int? page, int? pageSize, Guid? companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetSourceReportQuery(new PageRequest(page, pageSize, null), from, to, companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("GetRecruitmentSourceReport")
            .WithSummary("The applications of a period by source: interviewed, offered, hired and the hire rate")
            .ProducesValidationProblem();
        reports.MapGet("/sources/export", async (DateOnly from, DateOnly to, string? columns, Guid? companyId, IListExporter exporter, CancellationToken ct) =>
                (await exporter.ExportAsync(
                    new ExportList<SourceRow>(
                        "web.recruitment.reports.sources.title",
                        typeof(JobApplication),
                        RecruitmentReportExports.Sources,
                        paging => new GetSourceReportQuery(paging, from, to, companyId),
                        null,
                        ExportRules.ParseColumns(columns),
                        new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["companyId"] = companyId }),
                    ct)).ToFile())
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("ExportRecruitmentSourceReport")
            .WithSummary("The source report as Excel, with its filters and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/interviewers", async (DateOnly from, DateOnly to, int? page, int? pageSize, Guid? companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetInterviewerReportQuery(new PageRequest(page, pageSize, null), from, to, companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("GetRecruitmentInterviewerReport")
            .WithSummary("The interviews of a period by interviewer: completed, forms sent and missing, hours to the form")
            .ProducesValidationProblem();
        reports.MapGet("/interviewers/export", async (DateOnly from, DateOnly to, string? columns, Guid? companyId, IListExporter exporter, CancellationToken ct) =>
                (await exporter.ExportAsync(
                    new ExportList<InterviewerRow>(
                        "web.recruitment.reports.interviewers.title",
                        typeof(Interview),
                        RecruitmentReportExports.Interviewers,
                        paging => new GetInterviewerReportQuery(paging, from, to, companyId),
                        null,
                        ExportRules.ParseColumns(columns),
                        new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["companyId"] = companyId }),
                    ct)).ToFile())
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("ExportRecruitmentInterviewerReport")
            .WithSummary("The interviewer report as Excel, with its filters and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/requisition-aging", async (int? page, int? pageSize, Guid? companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetRequisitionAgingReportQuery(new PageRequest(page, pageSize, null), companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("GetRequisitionAgingReport")
            .WithSummary("The requisitions waiting for approval or open now, oldest first, with their age since they were sent")
            .ProducesValidationProblem();
        reports.MapGet("/requisition-aging/export", async (string? columns, Guid? companyId, IListExporter exporter, CancellationToken ct) =>
                (await exporter.ExportAsync(
                    new ExportList<RequisitionAgingRow>(
                        "web.recruitment.reports.requisition_aging.title",
                        typeof(JobRequisition),
                        RecruitmentReportExports.RequisitionAging,
                        paging => new GetRequisitionAgingReportQuery(paging, companyId),
                        null,
                        ExportRules.ParseColumns(columns),
                        new Dictionary<string, object?> { ["companyId"] = companyId }),
                    ct)).ToFile())
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("ExportRequisitionAgingReport")
            .WithSummary("The requisition aging report as Excel, with its filter and chosen columns")
            .ProducesValidationProblem();

        reports.MapGet("/offers", async (DateOnly from, DateOnly to, int? page, int? pageSize, Guid? companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOfferReportQuery(new PageRequest(page, pageSize, null), from, to, companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("GetRecruitmentOfferReport")
            .WithSummary("The offers sent in a period by opening: accepted, declined, expired, waiting and the acceptance rate")
            .ProducesValidationProblem();
        reports.MapGet("/offers/export", async (DateOnly from, DateOnly to, string? columns, Guid? companyId, IListExporter exporter, CancellationToken ct) =>
                (await exporter.ExportAsync(
                    new ExportList<OfferReportRow>(
                        "web.recruitment.reports.offers.title",
                        typeof(JobOffer),
                        RecruitmentReportExports.Offers,
                        paging => new GetOfferReportQuery(paging, from, to, companyId),
                        null,
                        ExportRules.ParseColumns(columns),
                        new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["companyId"] = companyId }),
                    ct)).ToFile())
            .RequirePermission(RecruitmentPermissions.Reports.Read)
            .WithName("ExportRecruitmentOfferReport")
            .WithSummary("The offer report as Excel, with its filters and chosen columns")
            .ProducesValidationProblem();
    }

    private static Results<Ok<T>, ProblemHttpResult> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
}
