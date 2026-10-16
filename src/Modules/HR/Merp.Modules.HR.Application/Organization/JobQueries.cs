using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>The tenant's job catalog, filtered and paged; shared by all companies, so every scope sees it (D37).</summary>
/// <remarks>Search matches part of the code or name.</remarks>
public sealed record GetJobsQuery(
    PageRequest Paging,
    string? Search = null,
    string? Family = null,
    bool? IsActive = null) : IQuery<PagedResult<JobListItem>>, IRequirePermission
{
    public const string DefaultSort = "name";

    public static readonly SortMap<Job> Sorts = SortMap<Job>.By(j => j.Id)
        .Add("code", j => j.Code)
        .Add("name", j => j.Name)
        .Add("family", j => j.Family)
        .Add("level", j => j.Level)
        .Add("isActive", j => j.IsActive)
        .Add("createdAt", j => j.CreatedAt);

    public string Permission => HrPermissions.Org.Read;
}

/// <remarks>PositionCount counts the positions with this job that are not closed, in every company.</remarks>
public sealed record JobListItem(Guid Id, string Code, string Name, string? Family, string? Level, bool IsActive, int PositionCount);

/// <summary>One job of the catalog.</summary>
public sealed record GetJobQuery(Guid Id) : IQuery<JobDetails>, IRequirePermission
{
    public string Permission => HrPermissions.Org.Read;
}

/// <remarks>Send Version back when updating (optimistic concurrency).</remarks>
public sealed record JobDetails(
    Guid Id,
    string Code,
    string Name,
    string? Family,
    string? Level,
    bool IsActive,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

internal sealed class GetJobsQueryValidator : AbstractValidator<GetJobsQuery>
{
    public GetJobsQueryValidator() =>
        RuleFor(q => q.Paging.Sort).Must(GetJobsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
}

internal sealed class GetJobsQueryHandler(IHrDbContext db) : IQueryHandler<GetJobsQuery, PagedResult<JobListItem>>
{
    public async Task<Result<PagedResult<JobListItem>>> Handle(GetJobsQuery request, CancellationToken cancellationToken)
    {
        var jobs = db.Jobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            jobs = jobs.Where(j => TextSearch.Matches(j.Code, pattern) || TextSearch.Matches(j.Name, pattern));
        }

        if (!string.IsNullOrWhiteSpace(request.Family))
        {
            var family = request.Family.Trim();
            jobs = jobs.Where(j => j.Family == family);
        }

        if (request.IsActive is { } isActive)
        {
            jobs = jobs.Where(j => j.IsActive == isActive);
        }

        var positions = db.Positions.AsNoTracking();
        return await GetJobsQuery.Sorts.Apply(jobs, request.Paging.Sort, GetJobsQuery.DefaultSort)
            .Select(j => new JobListItem(
                j.Id.Value,
                j.Code,
                j.Name,
                j.Family,
                j.Level,
                j.IsActive,
                positions.Count(p => p.JobId == j.Id && p.Status != PositionStatus.Closed)))
            .ToPagedResultAsync(request.Paging, cancellationToken);
    }
}

internal sealed class GetJobQueryHandler(IHrDbContext db) : IQueryHandler<GetJobQuery, JobDetails>
{
    public async Task<Result<JobDetails>> Handle(GetJobQuery request, CancellationToken cancellationToken)
    {
        var id = JobId.From(request.Id);
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new JobDetails(
                j.Id.Value,
                j.Code,
                j.Name,
                j.Family,
                j.Level,
                j.IsActive,
                EF.Property<uint>(j, ConcurrencyVersion.PropertyName),
                j.CreatedAt,
                j.ModifiedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return CommonErrors.NotFound;
        }

        return job;
    }
}
