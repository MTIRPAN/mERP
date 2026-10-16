using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>
/// Positions in the caller's scope with their seats, filtered and paged; the active company narrows the list (D4).
/// With <see cref="VacantOnly"/> it is the vacancy report: active positions with empty seats (D37).
/// </summary>
/// <param name="Paging">Page and sort.</param>
/// <param name="CompanyId">Only the positions of this company.</param>
/// <param name="OrgUnitId">Only the positions of this unit.</param>
/// <param name="IncludeSubUnits">With <paramref name="OrgUnitId"/>: also the positions of its sub units.</param>
/// <param name="JobId">Only the positions with this job.</param>
/// <param name="Status">Only positions in this state.</param>
/// <param name="Search">Part of the position code or of the job's name.</param>
/// <param name="VacantOnly">Only active positions with empty seats.</param>
public sealed record GetPositionsQuery(
    PageRequest Paging,
    Guid? CompanyId = null,
    Guid? OrgUnitId = null,
    bool IncludeSubUnits = false,
    Guid? JobId = null,
    PositionStatus? Status = null,
    string? Search = null,
    bool VacantOnly = false) : IQuery<PagedResult<PositionListItem>>, IRequirePermission
{
    public const string DefaultSort = "code";

    public static readonly SortMap<Position> Sorts = SortMap<Position>.By(p => p.Id)
        .Add("code", p => p.Code)
        .Add("norm", p => p.Norm)
        .Add("status", p => p.Status)
        .Add("createdAt", p => p.CreatedAt);

    public string Permission => HrPermissions.Org.Read;
}

/// <remarks>Filled counts the taken seats, Vacant the empty ones (0 unless the position is active).</remarks>
public sealed record PositionListItem(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Code,
    Guid OrgUnitId,
    string OrgUnitName,
    Guid JobId,
    string JobCode,
    string JobName,
    Guid? ReportsToPositionId,
    string? ReportsToPositionCode,
    int Norm,
    int Filled,
    int Vacant,
    PositionStatus Status,
    bool IsManager);

/// <summary>One position in the caller's scope with its holders; otherwise 404.</summary>
public sealed record GetPositionQuery(Guid Id) : IQuery<PositionDetails>, IRequirePermission
{
    public string Permission => HrPermissions.Org.Read;
}

/// <remarks>
/// ManagerPositionId is the position that manages it, from the reporting lines (D37). Holders are who hold it today;
/// empty until job assignments exist (H2). Send Version back when updating (optimistic concurrency).
/// </remarks>
public sealed record PositionDetails(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Code,
    Guid OrgUnitId,
    string OrgUnitName,
    Guid JobId,
    string JobCode,
    string JobName,
    Guid? ReportsToPositionId,
    string? ReportsToPositionCode,
    Guid? ManagerPositionId,
    string? ManagerPositionCode,
    int Norm,
    int Filled,
    int Vacant,
    PositionStatus Status,
    bool IsManager,
    IReadOnlyList<PositionHolderItem> Holders,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record PositionHolderItem(Guid EmployeeId, Guid? UserId, bool IsPrimary);

internal sealed class GetPositionsQueryValidator : AbstractValidator<GetPositionsQuery>
{
    public GetPositionsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetPositionsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleFor(q => q.Status).IsInEnum();
    }
}

/// <summary>A position with the names around it, as lists and details show it.</summary>
internal sealed record PositionRow(
    PositionId Id,
    Guid CompanyId,
    string Code,
    OrgUnitId OrgUnitId,
    string OrgUnitName,
    JobId JobId,
    string JobCode,
    string JobName,
    PositionId? ReportsToPositionId,
    string? ReportsToPositionCode,
    int Norm,
    PositionStatus Status,
    bool IsManager);

internal static class PositionRows
{
    /// <remarks>Names come from correlated subqueries, which keep the caller's sort.</remarks>
    public static IQueryable<PositionRow> Select(IQueryable<Position> positions, IHrDbContext db) =>
        positions.Select(p => new PositionRow(
            p.Id,
            p.CompanyId,
            p.Code,
            p.OrgUnitId,
            db.OrgUnits.Where(u => u.Id == p.OrgUnitId).Select(u => u.Name).First(),
            p.JobId,
            db.Jobs.Where(j => j.Id == p.JobId).Select(j => j.Code).First(),
            db.Jobs.Where(j => j.Id == p.JobId).Select(j => j.Name).First(),
            p.ReportsToPositionId,
            db.Positions.Where(m => m.Id == p.ReportsToPositionId).Select(m => m.Code).FirstOrDefault(),
            p.Norm,
            p.Status,
            p.IsManager));

    public static PositionListItem ToListItem(PositionRow row, string companyName, int filled) =>
        new(
            row.Id.Value,
            row.CompanyId,
            companyName,
            row.Code,
            row.OrgUnitId.Value,
            row.OrgUnitName,
            row.JobId.Value,
            row.JobCode,
            row.JobName,
            row.ReportsToPositionId?.Value,
            row.ReportsToPositionCode,
            row.Norm,
            filled,
            row.Status == PositionStatus.Active ? Math.Max(row.Norm - filled, 0) : 0,
            row.Status,
            row.IsManager);
}

internal sealed class GetPositionsQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IOrganizationLookup organization) : IQueryHandler<GetPositionsQuery, PagedResult<PositionListItem>>
{
    public async Task<Result<PagedResult<PositionListItem>>> Handle(GetPositionsQuery request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var positions = Filter(scope.FilterPositions(db.Positions.AsNoTracking(), db), request);
        var sorted = GetPositionsQuery.Sorts.Apply(positions, request.Paging.Sort, GetPositionsQuery.DefaultSort);

        List<PositionRow> rows;
        Dictionary<PositionId, int> filled;
        int total;
        if (request.VacantOnly)
        {
            // Holders do not live in this table (D37): read the seats of every candidate, keep the vacant ones, then page
            var seats = await sorted.Where(p => p.Status == PositionStatus.Active).Select(p => new { p.Id, p.Norm }).ToListAsync(cancellationToken);
            filled = await holders.CountFilledAsync([.. seats.Select(s => s.Id)], cancellationToken);
            var vacant = seats.Where(s => s.Norm > filled.GetValueOrDefault(s.Id)).Select(s => s.Id).ToList();
            total = vacant.Count;
            var pageIds = vacant.Skip(request.Paging.Skip).Take(request.Paging.Size).ToList();
            var order = pageIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            rows = [.. (await PositionRows.Select(db.Positions.AsNoTracking().Where(p => pageIds.Contains(p.Id)), db).ToListAsync(cancellationToken))
                .OrderBy(r => order[r.Id])];
        }
        else
        {
            var page = await PositionRows.Select(sorted, db).ToPagedResultAsync(request.Paging, cancellationToken);
            rows = [.. page.Items];
            total = page.TotalCount;
            filled = await holders.CountFilledAsync([.. rows.Select(r => r.Id)], cancellationToken);
        }

        var companies = await OrgReferences.CompaniesAsync(organization, rows.Select(r => r.CompanyId), cancellationToken);
        return new PagedResult<PositionListItem>(
            [.. rows.Select(r => PositionRows.ToListItem(r, companies.GetValueOrDefault(r.CompanyId)?.ShortName ?? string.Empty, filled.GetValueOrDefault(r.Id)))],
            request.Paging.PageNumber,
            request.Paging.Size,
            total);

        IQueryable<Position> Filter(IQueryable<Position> query, GetPositionsQuery filter)
        {
            foreach (var companyId in new[] { filter.CompanyId, companyContext.ActiveCompanyId }.OfType<Guid>().Distinct())
            {
                query = query.Where(p => p.CompanyId == companyId);
            }

            if (filter.OrgUnitId is { } orgUnitId)
            {
                var unitId = OrgUnitId.From(orgUnitId);
                if (filter.IncludeSubUnits)
                {
                    var units = db.OrgUnits;
                    query = query.Where(p => units.Any(u => u.Id == p.OrgUnitId
                        && units.Any(root => root.Id == unitId && OrgPaths.IsWithin(u.Path, root.Path))));
                }
                else
                {
                    query = query.Where(p => p.OrgUnitId == unitId);
                }
            }

            if (filter.JobId is { } jobFilter)
            {
                var jobId = JobId.From(jobFilter);
                query = query.Where(p => p.JobId == jobId);
            }

            if (filter.Status is { } status)
            {
                query = query.Where(p => p.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var pattern = TextSearch.ContainsPattern(filter.Search);
                var jobs = db.Jobs;
                query = query.Where(p => TextSearch.Matches(p.Code, pattern) || jobs.Any(j => j.Id == p.JobId && TextSearch.Matches(j.Name, pattern)));
            }

            return query;
        }
    }
}

internal sealed class GetPositionQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization) : IQueryHandler<GetPositionQuery, PositionDetails>
{
    public async Task<Result<PositionDetails>> Handle(GetPositionQuery request, CancellationToken cancellationToken)
    {
        var id = PositionId.From(request.Id);
        var scope = await OrgScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var row = await PositionRows.Select(scope.FilterPositions(db.Positions.AsNoTracking(), db).Where(p => p.Id == id), db)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return CommonErrors.NotFound;
        }

        var stamps = await db.Positions.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { Version = EF.Property<uint>(p, ConcurrencyVersion.PropertyName), p.CreatedAt, p.ModifiedAt })
            .SingleAsync(cancellationToken);
        var manager = await db.ReportingLines.AsNoTracking()
            .Where(r => r.SubordinatePositionId == id && r.Depth == 1)
            .Join(db.Positions, r => r.ManagerPositionId, p => p.Id, (r, p) => new { p.Id, p.Code })
            .SingleOrDefaultAsync(cancellationToken);
        var holderList = await holders.GetHoldersAsync([id], cancellationToken);
        var company = (await organization.GetCompaniesAsync([row.CompanyId], cancellationToken)).SingleOrDefault();
        var item = PositionRows.ToListItem(row, company?.ShortName ?? string.Empty, holderList.Select(h => h.EmployeeId).Distinct().Count());
        return new PositionDetails(
            item.Id,
            item.CompanyId,
            item.CompanyName,
            item.Code,
            item.OrgUnitId,
            item.OrgUnitName,
            item.JobId,
            item.JobCode,
            item.JobName,
            item.ReportsToPositionId,
            item.ReportsToPositionCode,
            manager?.Id.Value,
            manager?.Code,
            item.Norm,
            item.Filled,
            item.Vacant,
            item.Status,
            item.IsManager,
            [.. holderList.OrderByDescending(h => h.IsPrimary).Select(h => new PositionHolderItem(h.EmployeeId, h.UserId, h.IsPrimary))],
            stamps.Version,
            stamps.CreatedAt,
            stamps.ModifiedAt);
    }
}
