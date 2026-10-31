using System.Globalization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Reports;

/// <summary>The headline figures of the recruitment dashboard (PLAN R9, D56).</summary>
/// <param name="Applications">Applications made in the period.</param>
/// <param name="Hires">Applications hired in the period.</param>
/// <param name="AverageDaysToHire">From the application to the hire, on average.</param>
/// <param name="AverageDaysToFill">From the opening's publication to the hire, on average.</param>
/// <param name="OfferAcceptanceRate">Accepted among the offers sent in the period and answered or expired, in percent.</param>
/// <param name="OpenRequisitions">Requisitions waiting for approval or approved and not filled, now.</param>
/// <param name="MissingForms">Evaluation forms not sent for the completed interviews of the period.</param>
public sealed record RecruitmentSummary(
    int Applications,
    int Hires,
    decimal? AverageDaysToHire,
    decimal? AverageDaysToFill,
    decimal? OfferAcceptanceRate,
    int OpenRequisitions,
    int MissingForms);

/// <summary>
/// A stage of an opening's funnel: how many applications of the period reached it and are at it now. The conversion
/// is reached over the previous working stage's reached, in percent; none for the first stage and the final ones.
/// The id joins the opening and the stage order.
/// </summary>
public sealed record FunnelRow(
    string Id,
    Guid OpeningId,
    string OpeningTitle,
    int StageOrder,
    string StageName,
    StageType StageType,
    int Reached,
    decimal? ConversionRate,
    int Current);

/// <summary>A hire of the period with its durations.</summary>
public sealed record HireRow(
    Guid ApplicationId,
    string CandidateName,
    Guid OpeningId,
    string OpeningTitle,
    string Source,
    DateTimeOffset AppliedAt,
    DateTimeOffset HiredAt,
    decimal DaysToHire,
    decimal? DaysToFill);

/// <summary>A candidate source: how its applications of the period went.</summary>
public sealed record SourceRow(string Source, string? SourceName, int Applications, int Interviewed, int Offered, int Hired, decimal? HireRate);

/// <summary>An interviewer's interviews of the period, their evaluation forms and the hours from an interview's end to its form.</summary>
public sealed record InterviewerRow(Guid UserId, string? Name, int Interviews, int Completed, int FormsSent, int FormsMissing, decimal? AverageFeedbackHours);

/// <summary>An open requisition with its age in days since it was sent for approval, and the bucket of the age.</summary>
public sealed record RequisitionAgingRow(
    Guid Id,
    string Number,
    string PositionTitle,
    string? CompanyName,
    RequisitionStatus Status,
    int Headcount,
    int FilledCount,
    int AgeDays,
    string AgeBucket);

/// <summary>The offers sent in the period by opening, how they ended and accepted over accepted, declined and expired.</summary>
public sealed record OfferReportRow(Guid OpeningId, string OpeningTitle, int Sent, int Accepted, int Declined, int Expired, int Waiting, decimal? AcceptanceRate);

/// <summary>Filters of the reports: a period (inclusive, UTC days), a company in scope and an opening.</summary>
public interface IReportPeriod
{
    DateOnly From { get; }

    DateOnly To { get; }

    Guid? CompanyId { get; }
}

public sealed record GetRecruitmentSummaryQuery(DateOnly From, DateOnly To, Guid? CompanyId = null) : IQuery<RecruitmentSummary>, IRequirePermission, IReportPeriod
{
    public string Permission => RecruitmentPermissions.Reports.Read;
}

public sealed record GetFunnelReportQuery(PageRequest Paging, DateOnly From, DateOnly To, Guid? CompanyId = null, Guid? OpeningId = null)
    : IQuery<PagedResult<FunnelRow>>, IRequirePermission, IReportPeriod
{
    public string Permission => RecruitmentPermissions.Reports.Read;
}

public sealed record GetHireReportQuery(PageRequest Paging, DateOnly From, DateOnly To, Guid? CompanyId = null, Guid? OpeningId = null)
    : IQuery<PagedResult<HireRow>>, IRequirePermission, IReportPeriod
{
    public string Permission => RecruitmentPermissions.Reports.Read;
}

public sealed record GetSourceReportQuery(PageRequest Paging, DateOnly From, DateOnly To, Guid? CompanyId = null)
    : IQuery<PagedResult<SourceRow>>, IRequirePermission, IReportPeriod
{
    public string Permission => RecruitmentPermissions.Reports.Read;
}

public sealed record GetInterviewerReportQuery(PageRequest Paging, DateOnly From, DateOnly To, Guid? CompanyId = null)
    : IQuery<PagedResult<InterviewerRow>>, IRequirePermission, IReportPeriod
{
    public string Permission => RecruitmentPermissions.Reports.Read;
}

public sealed record GetRequisitionAgingReportQuery(PageRequest Paging, Guid? CompanyId = null)
    : IQuery<PagedResult<RequisitionAgingRow>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Reports.Read;
}

public sealed record GetOfferReportQuery(PageRequest Paging, DateOnly From, DateOnly To, Guid? CompanyId = null)
    : IQuery<PagedResult<OfferReportRow>>, IRequirePermission, IReportPeriod
{
    public string Permission => RecruitmentPermissions.Reports.Read;
}

/// <summary>The columns of the reports' Excel files; keys are those of the report columns on the screen.</summary>
public static class RecruitmentReportExports
{
    private const string Prefix = "web.recruitment.reports.columns.";

    public static readonly IReadOnlyList<ExportColumn<FunnelRow>> Funnel =
    [
        ExportColumn<FunnelRow>.Of("openingTitle", Prefix + "opening", r => r.OpeningTitle),
        ExportColumn<FunnelRow>.Of("stageName", Prefix + "stage", r => r.StageName),
        ExportColumn<FunnelRow>.Of("reached", Prefix + "reached", r => r.Reached),
        ExportColumn<FunnelRow>.Of("conversionRate", Prefix + "conversion_rate", r => r.ConversionRate),
        ExportColumn<FunnelRow>.Of("current", Prefix + "current", r => r.Current),
    ];

    public static readonly IReadOnlyList<ExportColumn<HireRow>> Hires =
    [
        ExportColumn<HireRow>.Of("candidateName", Prefix + "candidate", r => r.CandidateName),
        ExportColumn<HireRow>.Of("openingTitle", Prefix + "opening", r => r.OpeningTitle),
        ExportColumn<HireRow>.Of("source", Prefix + "source", r => r.Source),
        ExportColumn<HireRow>.Of("appliedAt", Prefix + "applied_at", r => DateOnly.FromDateTime(r.AppliedAt.UtcDateTime)),
        ExportColumn<HireRow>.Of("hiredAt", Prefix + "hired_at", r => DateOnly.FromDateTime(r.HiredAt.UtcDateTime)),
        ExportColumn<HireRow>.Of("daysToHire", Prefix + "days_to_hire", r => r.DaysToHire),
        ExportColumn<HireRow>.Of("daysToFill", Prefix + "days_to_fill", r => r.DaysToFill),
    ];

    public static readonly IReadOnlyList<ExportColumn<SourceRow>> Sources =
    [
        ExportColumn<SourceRow>.Of("sourceName", Prefix + "source", r => r.SourceName ?? r.Source),
        ExportColumn<SourceRow>.Of("applications", Prefix + "applications", r => r.Applications),
        ExportColumn<SourceRow>.Of("interviewed", Prefix + "interviewed", r => r.Interviewed),
        ExportColumn<SourceRow>.Of("offered", Prefix + "offered", r => r.Offered),
        ExportColumn<SourceRow>.Of("hired", Prefix + "hired", r => r.Hired),
        ExportColumn<SourceRow>.Of("hireRate", Prefix + "hire_rate", r => r.HireRate),
    ];

    public static readonly IReadOnlyList<ExportColumn<InterviewerRow>> Interviewers =
    [
        ExportColumn<InterviewerRow>.Of("name", Prefix + "interviewer", r => r.Name),
        ExportColumn<InterviewerRow>.Of("interviews", Prefix + "interviews", r => r.Interviews),
        ExportColumn<InterviewerRow>.Of("completed", Prefix + "completed", r => r.Completed),
        ExportColumn<InterviewerRow>.Of("formsSent", Prefix + "forms_sent", r => r.FormsSent),
        ExportColumn<InterviewerRow>.Of("formsMissing", Prefix + "forms_missing", r => r.FormsMissing),
        ExportColumn<InterviewerRow>.Of("averageFeedbackHours", Prefix + "average_feedback_hours", r => r.AverageFeedbackHours),
    ];

    public static readonly IReadOnlyList<ExportColumn<RequisitionAgingRow>> RequisitionAging =
    [
        ExportColumn<RequisitionAgingRow>.Of("number", Prefix + "number", r => r.Number),
        ExportColumn<RequisitionAgingRow>.Of("positionTitle", Prefix + "position", r => r.PositionTitle),
        ExportColumn<RequisitionAgingRow>.Of("companyName", Prefix + "company", r => r.CompanyName),
        ExportColumn<RequisitionAgingRow>.Of("status", Prefix + "status", r => r.Status, "web.recruitment.requisitions.status."),
        ExportColumn<RequisitionAgingRow>.Of("headcount", Prefix + "headcount", r => r.Headcount),
        ExportColumn<RequisitionAgingRow>.Of("filledCount", Prefix + "filled", r => r.FilledCount),
        ExportColumn<RequisitionAgingRow>.Of("ageDays", Prefix + "age_days", r => r.AgeDays),
        ExportColumn<RequisitionAgingRow>.Of("ageBucket", Prefix + "age_bucket", r => r.AgeBucket),
    ];

    public static readonly IReadOnlyList<ExportColumn<OfferReportRow>> Offers =
    [
        ExportColumn<OfferReportRow>.Of("openingTitle", Prefix + "opening", r => r.OpeningTitle),
        ExportColumn<OfferReportRow>.Of("sent", Prefix + "sent", r => r.Sent),
        ExportColumn<OfferReportRow>.Of("accepted", Prefix + "accepted", r => r.Accepted),
        ExportColumn<OfferReportRow>.Of("declined", Prefix + "declined", r => r.Declined),
        ExportColumn<OfferReportRow>.Of("expired", Prefix + "expired", r => r.Expired),
        ExportColumn<OfferReportRow>.Of("waiting", Prefix + "waiting", r => r.Waiting),
        ExportColumn<OfferReportRow>.Of("acceptanceRate", Prefix + "acceptance_rate", r => r.AcceptanceRate),
    ];
}

internal abstract class ReportPeriodValidator<T> : AbstractValidator<T>
    where T : IReportPeriod
{
    public const int MaxDays = 731;

    protected ReportPeriodValidator()
    {
        RuleFor(q => q.From).NotEmpty();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).WithErrorCode(CommonErrors.InvalidDateRange.Code);
        RuleFor(q => q.To)
            .Must((q, to) => to.DayNumber - q.From.DayNumber <= MaxDays).WithErrorCode("recruitment.reports.errors.period_too_long");
    }
}

internal sealed class GetRecruitmentSummaryQueryValidator : ReportPeriodValidator<GetRecruitmentSummaryQuery>;

internal sealed class GetFunnelReportQueryValidator : ReportPeriodValidator<GetFunnelReportQuery>;

internal sealed class GetHireReportQueryValidator : ReportPeriodValidator<GetHireReportQuery>;

internal sealed class GetSourceReportQueryValidator : ReportPeriodValidator<GetSourceReportQuery>;

internal sealed class GetInterviewerReportQueryValidator : ReportPeriodValidator<GetInterviewerReportQuery>;

internal sealed class GetOfferReportQueryValidator : ReportPeriodValidator<GetOfferReportQuery>;

/// <summary>
/// Computes the recruitment reports (D56) over the companies in the caller's scope of <c>recruitment.reports.read</c>.
/// The data of a tenant's recruitment is small enough to count in memory after the filters run in SQL.
/// </summary>
public sealed class RecruitmentReportData(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    IUserDirectory users,
    RequisitionService requisitions,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset End(DateOnly day) => Start(day.AddDays(1));

    private static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round(part * 100m / whole, 1);

    private static decimal Days(TimeSpan span) => Math.Round((decimal)span.TotalDays, 1);

    public async Task<List<FunnelRow>> FunnelAsync(IReportPeriod period, Guid? openingId, CancellationToken cancellationToken)
    {
        var applications = await ApplicationsAsync(period, openingId, cancellationToken);
        var openings = await OpeningsAsync(applications.Select(a => a.OpeningId), cancellationToken);
        var culture = currentUser.Culture;
        var rows = new List<FunnelRow>();
        foreach (var group in applications.GroupBy(a => a.OpeningId).OrderBy(g => openings[g.Key].Title, StringComparer.Ordinal))
        {
            var opening = openings[group.Key];

            // The working stages in pipeline order, the Hired stage last; an application reached every working stage
            // up to the furthest one it entered, so skipped stages count too
            var working = opening.Stages.Where(s => !s.IsFinal).Append(opening.Stages.First(s => s.Type == StageType.Hired)).Select(s => s.Id).ToList();
            var furthest = group.Select(a => a.History.Select(h => working.IndexOf(h.ToStageId)).DefaultIfEmpty(-1).Max()).ToList();
            int? previous = null;
            var order = 0;
            foreach (var stage in opening.Stages.OrderBy(s => s.IsFinal && s.Type != StageType.Hired))
            {
                var index = working.IndexOf(stage.Id);
                var reached = index >= 0 ? furthest.Count(f => f >= index) : group.Count(a => a.History.Any(h => h.ToStageId == stage.Id));
                rows.Add(new FunnelRow(
                    string.Create(CultureInfo.InvariantCulture, $"{opening.Id.Value:N}-{order + 1}"),
                    opening.Id.Value,
                    opening.Title,
                    ++order,
                    stage.Name.Get(culture, "tr-TR"),
                    stage.Type,
                    reached,
                    index >= 0 && previous is { } before ? Rate(reached, before) : null,
                    group.Count(a => a.StageId == stage.Id)));
                if (index >= 0)
                {
                    previous = reached;
                }
            }
        }

        return rows;
    }

    public async Task<List<HireRow>> HiresAsync(IReportPeriod period, Guid? openingId, CancellationToken cancellationToken)
    {
        var (from, to) = (Start(period.From), End(period.To));
        var query = (await ScopedApplicationsAsync(period.CompanyId, openingId, cancellationToken))
            .Where(a => a.Status == ApplicationStatus.Hired && a.History.Any(h => h.Status == ApplicationStatus.Hired && h.At >= from && h.At < to));
        var hired = await query.ToListAsync(cancellationToken);
        var openings = await OpeningsAsync(hired.Select(a => a.OpeningId), cancellationToken);
        var candidateIds = hired.Select(a => a.CandidateId).Distinct().ToList();
        var names = await db.Candidates.AsNoTracking().Where(c => candidateIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken);
        return
        [
            .. hired.Select(a =>
            {
                var hiredAt = a.History.Where(h => h.Status == ApplicationStatus.Hired).Max(h => h.At);
                var opening = openings[a.OpeningId];
                return new HireRow(
                    a.Id.Value,
                    names.GetValueOrDefault(a.CandidateId, string.Empty),
                    opening.Id.Value,
                    opening.Title,
                    a.Source,
                    a.AppliedAt,
                    hiredAt,
                    Days(hiredAt - a.AppliedAt),
                    opening.PublishedAt is { } published ? Days(hiredAt - published) : null);
            }).OrderBy(r => r.HiredAt),
        ];
    }

    public async Task<List<SourceRow>> SourcesAsync(IReportPeriod period, CancellationToken cancellationToken)
    {
        var applications = await ApplicationsAsync(period, null, cancellationToken);
        var ids = applications.Select(a => a.Id).ToList();
        var interviewed = (await db.Interviews.AsNoTracking()
                .Where(i => ids.Contains(i.ApplicationId) && i.Status != InterviewStatus.Cancelled)
                .Select(i => i.ApplicationId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var offered = (await db.JobOffers.AsNoTracking()
                .Where(o => ids.Contains(o.ApplicationId) && o.SentAt != null)
                .Select(o => o.ApplicationId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var codes = applications.Select(a => a.Source).Distinct().ToList();
        var names = (await db.Lookups.AsNoTracking().Where(l => l.Kind == LookupKind.CandidateSource && codes.Contains(l.Code)).ToListAsync(cancellationToken))
            .ToDictionary(l => l.Code, l => l.Name.Get(currentUser.Culture, "tr-TR"), StringComparer.Ordinal);
        return
        [
            .. applications.GroupBy(a => a.Source, StringComparer.Ordinal).Select(g =>
            {
                var hired = g.Count(a => a.Status == ApplicationStatus.Hired);
                return new SourceRow(
                    g.Key,
                    names.GetValueOrDefault(g.Key),
                    g.Count(),
                    g.Count(a => interviewed.Contains(a.Id)),
                    g.Count(a => offered.Contains(a.Id)),
                    hired,
                    Rate(hired, g.Count()));
            }).OrderByDescending(r => r.Applications).ThenBy(r => r.Source, StringComparer.Ordinal),
        ];
    }

    public async Task<List<InterviewerRow>> InterviewersAsync(IReportPeriod period, CancellationToken cancellationToken)
    {
        var (from, to) = (Start(period.From), End(period.To));
        var query = db.Interviews.AsNoTracking().Where(i => i.Status != InterviewStatus.Cancelled && i.StartsAt >= from && i.StartsAt < to);
        if (await CompaniesAsync(cancellationToken) is { } companies)
        {
            query = query.Where(i => companies.Contains(i.CompanyId));
        }

        if (period.CompanyId is { } company)
        {
            query = query.Where(i => i.CompanyId == company);
        }

        var interviews = await query.ToListAsync(cancellationToken);
        var interviewIds = interviews.Select(i => i.Id).ToList();
        var forms = await db.InterviewFeedbacks.AsNoTracking().Where(f => interviewIds.Contains(f.InterviewId)).ToListAsync(cancellationToken);
        var byInterview = interviews.ToDictionary(i => i.Id);
        var people = interviews.SelectMany(i => i.InterviewerUserIds).Distinct().ToList();
        var names = (await users.GetAsync(people, cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);
        return
        [
            .. people.Select(userId =>
            {
                var theirs = interviews.Where(i => i.InterviewerUserIds.Contains(userId)).ToList();
                var sent = forms.Where(f => f.InterviewerUserId == userId).ToList();
                var missing = theirs.Count(i => i.Status == InterviewStatus.Completed && i.Evaluation is not null && sent.All(f => f.InterviewId != i.Id));
                var delays = sent.Select(f => Math.Max(0, (f.SubmittedAt - byInterview[f.InterviewId].EndsAt).TotalHours)).ToList();
                return new InterviewerRow(
                    userId,
                    names.GetValueOrDefault(userId),
                    theirs.Count,
                    theirs.Count(i => i.Status == InterviewStatus.Completed),
                    sent.Count,
                    missing,
                    delays.Count == 0 ? null : Math.Round((decimal)delays.Average(), 1));
            }).OrderByDescending(r => r.Interviews).ThenBy(r => r.Name, StringComparer.Ordinal),
        ];
    }

    public async Task<List<RequisitionAgingRow>> RequisitionAgingAsync(Guid? companyId, CancellationToken cancellationToken)
    {
        var query = db.JobRequisitions.AsNoTracking()
            .Where(r => r.Status == RequisitionStatus.Pending || r.Status == RequisitionStatus.Returned || r.Status == RequisitionStatus.Approved);
        if (await CompaniesAsync(cancellationToken) is { } companies)
        {
            query = query.Where(r => companies.Contains(r.CompanyId));
        }

        if (companyId is { } company)
        {
            query = query.Where(r => r.CompanyId == company);
        }

        var open = await query.ToListAsync(cancellationToken);
        var items = (await requisitions.RowsAsync(open, cancellationToken)).ToDictionary(r => r.Id);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return
        [
            .. open.Select(r =>
            {
                var item = items[r.Id.Value];
                var since = DateOnly.FromDateTime((r.SubmittedAt ?? r.CreatedAt).UtcDateTime);
                var age = Math.Max(0, today.DayNumber - since.DayNumber);
                return new RequisitionAgingRow(r.Id.Value, r.Number, item.PositionTitle, item.CompanyName, r.Status, r.Headcount, r.FilledCount, age, Bucket(age));
            }).OrderByDescending(r => r.AgeDays),
        ];
    }

    public async Task<List<OfferReportRow>> OffersAsync(IReportPeriod period, CancellationToken cancellationToken)
    {
        var (from, to) = (Start(period.From), End(period.To));
        var query = db.JobOffers.AsNoTracking().Where(o => o.SentAt >= from && o.SentAt < to);
        if (await CompaniesAsync(cancellationToken) is { } companies)
        {
            query = query.Where(o => companies.Contains(o.CompanyId));
        }

        if (period.CompanyId is { } company)
        {
            query = query.Where(o => o.CompanyId == company);
        }

        var offers = await query.ToListAsync(cancellationToken);
        var openings = await OpeningsAsync(offers.Select(o => o.OpeningId), cancellationToken);
        return
        [
            .. offers.GroupBy(o => o.OpeningId).Select(g =>
            {
                var accepted = g.Count(o => o.Status == OfferStatus.Accepted);
                var declined = g.Count(o => o.Status == OfferStatus.Declined);
                var expired = g.Count(o => o.Status == OfferStatus.Expired);
                return new OfferReportRow(
                    g.Key.Value,
                    openings[g.Key].Title,
                    g.Count(),
                    accepted,
                    declined,
                    expired,
                    g.Count(o => o.Status == OfferStatus.Sent),
                    Rate(accepted, accepted + declined + expired));
            }).OrderBy(r => r.OpeningTitle, StringComparer.Ordinal),
        ];
    }

    public async Task<RecruitmentSummary> SummaryAsync(IReportPeriod period, CancellationToken cancellationToken)
    {
        var applications = await ApplicationsAsync(period, null, cancellationToken);
        var hires = await HiresAsync(period, null, cancellationToken);
        var offers = await OffersAsync(period, cancellationToken);
        var interviewers = await InterviewersAsync(period, cancellationToken);
        var aging = await RequisitionAgingAsync(period.CompanyId, cancellationToken);
        var answered = offers.Sum(o => o.Accepted + o.Declined + o.Expired);
        var fill = hires.Where(h => h.DaysToFill is not null).Select(h => h.DaysToFill!.Value).ToList();
        return new RecruitmentSummary(
            applications.Count,
            hires.Count,
            hires.Count == 0 ? null : Math.Round(hires.Average(h => h.DaysToHire), 1),
            fill.Count == 0 ? null : Math.Round(fill.Average(), 1),
            Rate(offers.Sum(o => o.Accepted), answered),
            aging.Count,
            interviewers.Sum(i => i.FormsMissing));
    }

    public static PagedResult<T> Page<T>(IReadOnlyList<T> rows, PageRequest paging) =>
        new([.. rows.Skip(paging.Skip).Take(paging.Size)], paging.PageNumber, paging.Size, rows.Count);

    private static string Bucket(int days) => days switch
    {
        <= 30 => "0-30",
        <= 60 => "31-60",
        <= 90 => "61-90",
        _ => "90+",
    };

    private async Task<List<JobApplication>> ApplicationsAsync(IReportPeriod period, Guid? openingId, CancellationToken cancellationToken)
    {
        var (from, to) = (Start(period.From), End(period.To));
        var query = await ScopedApplicationsAsync(period.CompanyId, openingId, cancellationToken);
        return await query.Where(a => a.AppliedAt >= from && a.AppliedAt < to).ToListAsync(cancellationToken);
    }

    /// <summary>The applications of the companies in scope with their stage history, of a company and an opening when given.</summary>
    private async Task<IQueryable<JobApplication>> ScopedApplicationsAsync(Guid? companyId, Guid? openingId, CancellationToken cancellationToken)
    {
        var query = db.JobApplications.AsNoTracking().Include(a => a.History).AsQueryable();
        if (await CompaniesAsync(cancellationToken) is { } companies)
        {
            query = query.Where(a => companies.Contains(a.CompanyId));
        }

        if (companyId is { } company)
        {
            query = query.Where(a => a.CompanyId == company);
        }

        if (openingId is { } id)
        {
            var opening = JobOpeningId.From(id);
            query = query.Where(a => a.OpeningId == opening);
        }

        return query;
    }

    private async Task<Dictionary<JobOpeningId, JobOpening>> OpeningsAsync(IEnumerable<JobOpeningId> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return await db.JobOpenings.AsNoTracking().Where(o => list.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
    }

    /// <summary>The companies in scope; null for the whole tenant.</summary>
    private async Task<List<Guid>?> CompaniesAsync(CancellationToken cancellationToken) =>
        await RecruitmentSettingRules.CompaniesAsync(scopes, RecruitmentPermissions.Reports.Read, cancellationToken) is { } companies ? [.. companies] : null;
}

internal sealed class GetRecruitmentSummaryQueryHandler(RecruitmentReportData data) : IQueryHandler<GetRecruitmentSummaryQuery, RecruitmentSummary>
{
    public async Task<Result<RecruitmentSummary>> Handle(GetRecruitmentSummaryQuery request, CancellationToken cancellationToken) =>
        await data.SummaryAsync(request, cancellationToken);
}

internal sealed class GetFunnelReportQueryHandler(RecruitmentReportData data) : IQueryHandler<GetFunnelReportQuery, PagedResult<FunnelRow>>
{
    public async Task<Result<PagedResult<FunnelRow>>> Handle(GetFunnelReportQuery request, CancellationToken cancellationToken) =>
        RecruitmentReportData.Page(await data.FunnelAsync(request, request.OpeningId, cancellationToken), request.Paging);
}

internal sealed class GetHireReportQueryHandler(RecruitmentReportData data) : IQueryHandler<GetHireReportQuery, PagedResult<HireRow>>
{
    public async Task<Result<PagedResult<HireRow>>> Handle(GetHireReportQuery request, CancellationToken cancellationToken) =>
        RecruitmentReportData.Page(await data.HiresAsync(request, request.OpeningId, cancellationToken), request.Paging);
}

internal sealed class GetSourceReportQueryHandler(RecruitmentReportData data) : IQueryHandler<GetSourceReportQuery, PagedResult<SourceRow>>
{
    public async Task<Result<PagedResult<SourceRow>>> Handle(GetSourceReportQuery request, CancellationToken cancellationToken) =>
        RecruitmentReportData.Page(await data.SourcesAsync(request, cancellationToken), request.Paging);
}

internal sealed class GetInterviewerReportQueryHandler(RecruitmentReportData data) : IQueryHandler<GetInterviewerReportQuery, PagedResult<InterviewerRow>>
{
    public async Task<Result<PagedResult<InterviewerRow>>> Handle(GetInterviewerReportQuery request, CancellationToken cancellationToken) =>
        RecruitmentReportData.Page(await data.InterviewersAsync(request, cancellationToken), request.Paging);
}

internal sealed class GetRequisitionAgingReportQueryHandler(RecruitmentReportData data) : IQueryHandler<GetRequisitionAgingReportQuery, PagedResult<RequisitionAgingRow>>
{
    public async Task<Result<PagedResult<RequisitionAgingRow>>> Handle(GetRequisitionAgingReportQuery request, CancellationToken cancellationToken) =>
        RecruitmentReportData.Page(await data.RequisitionAgingAsync(request.CompanyId, cancellationToken), request.Paging);
}

internal sealed class GetOfferReportQueryHandler(RecruitmentReportData data) : IQueryHandler<GetOfferReportQuery, PagedResult<OfferReportRow>>
{
    public async Task<Result<PagedResult<OfferReportRow>>> Handle(GetOfferReportQuery request, CancellationToken cancellationToken) =>
        RecruitmentReportData.Page(await data.OffersAsync(request, cancellationToken), request.Paging);
}
