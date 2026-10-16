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
/// The units of one company in the caller's scope with the staffing of their active positions, for the
/// organization tree (D37). The company is the given one, otherwise the active company (<c>X-Company-Id</c>, D4).
/// Units whose parent is outside the scope come without their parent.
/// </summary>
public sealed record GetOrgUnitTreeQuery(Guid? CompanyId = null) : IQuery<OrgUnitTree>, IRequirePermission
{
    public string Permission => HrPermissions.Org.Read;
}

public sealed record OrgUnitTree(Guid CompanyId, IReadOnlyList<OrgUnitTreeNode> Units);

/// <param name="Id">The unit.</param>
/// <param name="ParentId">The parent; null at the top or when the parent is outside the caller's scope.</param>
/// <param name="Code">Unit code.</param>
/// <param name="Name">Unit name.</param>
/// <param name="Type">Directorate, Department, Unit or Team.</param>
/// <param name="ManagerPositionId">The manager position.</param>
/// <param name="ManagerPositionCode">Its code.</param>
/// <param name="ManagerJobName">Its job title.</param>
/// <param name="CostCenterCode">Cost center code.</param>
/// <param name="BranchId">The unit's branch.</param>
/// <param name="ValidFrom">First day.</param>
/// <param name="ValidTo">Last day.</param>
/// <param name="IsActive">Exists today in the company's time zone.</param>
/// <param name="PositionCount">Positions that are not closed.</param>
/// <param name="Norm">Seats of the active positions.</param>
/// <param name="Filled">Taken seats of the active positions.</param>
/// <param name="Vacant">Empty seats of the active positions.</param>
public sealed record OrgUnitTreeNode(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    OrgUnitType Type,
    Guid? ManagerPositionId,
    string? ManagerPositionCode,
    string? ManagerJobName,
    string? CostCenterCode,
    Guid? BranchId,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    int PositionCount,
    int Norm,
    int Filled,
    int Vacant);

/// <summary>Units in the caller's scope, filtered and paged; the active company narrows the list (D4).</summary>
/// <remarks>Search matches part of the code or name.</remarks>
public sealed record GetOrgUnitsQuery(
    PageRequest Paging,
    Guid? CompanyId = null,
    string? Search = null,
    OrgUnitType? Type = null,
    Guid? ParentId = null) : IQuery<PagedResult<OrgUnitListItem>>, IRequirePermission
{
    public const string DefaultSort = "name";

    public static readonly SortMap<OrgUnit> Sorts = SortMap<OrgUnit>.By(u => u.Id)
        .Add("code", u => u.Code)
        .Add("name", u => u.Name)
        .Add("type", u => u.Type)
        .Add("createdAt", u => u.CreatedAt);

    public string Permission => HrPermissions.Org.Read;
}

public sealed record OrgUnitListItem(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Code,
    string Name,
    OrgUnitType Type,
    Guid? ParentId,
    string? ParentName,
    bool IsActive);

/// <summary>One unit in the caller's scope; otherwise 404.</summary>
public sealed record GetOrgUnitQuery(Guid Id) : IQuery<OrgUnitDetails>, IRequirePermission
{
    public string Permission => HrPermissions.Org.Read;
}

/// <remarks>Send Version back when updating or moving (optimistic concurrency).</remarks>
public sealed record OrgUnitDetails(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    Guid? ParentId,
    string? ParentName,
    string Code,
    string Name,
    OrgUnitType Type,
    Guid? ManagerPositionId,
    string? ManagerPositionCode,
    string? ManagerJobName,
    string? CostCenterCode,
    Guid? BranchId,
    string? BranchName,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

internal sealed class GetOrgUnitsQueryValidator : AbstractValidator<GetOrgUnitsQuery>
{
    public GetOrgUnitsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetOrgUnitsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleFor(q => q.Type).IsInEnum();
    }
}

internal sealed class GetOrgUnitTreeQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : IQueryHandler<GetOrgUnitTreeQuery, OrgUnitTree>
{
    public async Task<Result<OrgUnitTree>> Handle(GetOrgUnitTreeQuery request, CancellationToken cancellationToken)
    {
        if ((request.CompanyId ?? companyContext.ActiveCompanyId) is not { } companyId)
        {
            return Error.Validation([new FieldError("companyId", CommonErrors.Required.Code)]);
        }

        var scope = await OrgScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var units = await scope.FilterUnits(db.OrgUnits.AsNoTracking(), db)
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.Name)
            .ThenBy(u => u.Code)
            .Select(u => new
            {
                u.Id,
                u.ParentId,
                u.Code,
                u.Name,
                u.Type,
                u.ManagerPositionId,
                ManagerPositionCode = db.Positions.Where(p => p.Id == u.ManagerPositionId).Select(p => p.Code).FirstOrDefault(),
                ManagerJobName = db.Positions.Where(p => p.Id == u.ManagerPositionId)
                    .Join(db.Jobs, p => p.JobId, j => j.Id, (p, j) => j.Name)
                    .FirstOrDefault(),
                u.CostCenterCode,
                u.BranchId,
                u.ValidFrom,
                u.ValidTo,
            })
            .ToListAsync(cancellationToken);

        var unitIds = units.Select(u => u.Id).ToList();
        var positions = await db.Positions.AsNoTracking()
            .Where(p => unitIds.Contains(p.OrgUnitId) && p.Status != PositionStatus.Closed)
            .Select(p => new { p.Id, p.OrgUnitId, p.Norm, p.Status })
            .ToListAsync(cancellationToken);
        var filled = await holders.CountFilledAsync([.. positions.Select(p => p.Id)], cancellationToken);
        var staffing = positions.GroupBy(p => p.OrgUnitId).ToDictionary(g => g.Key, g =>
        {
            var active = g.Where(p => p.Status == PositionStatus.Active).ToList();
            var taken = active.Sum(p => filled.GetValueOrDefault(p.Id));
            var vacant = active.Sum(p => Math.Max(p.Norm - filled.GetValueOrDefault(p.Id), 0));
            return (Count: g.Count(), Norm: active.Sum(p => p.Norm), Filled: taken, Vacant: vacant);
        });

        var company = (await organization.GetCompaniesAsync([companyId], cancellationToken)).SingleOrDefault();
        var today = HrClock.Today(timeProvider, company?.TimeZoneId);
        var visible = unitIds.ToHashSet();
        return new OrgUnitTree(companyId, [.. units.Select(u =>
        {
            var numbers = staffing.GetValueOrDefault(u.Id);
            return new OrgUnitTreeNode(
                u.Id.Value,
                u.ParentId is { } parent && visible.Contains(parent) ? parent.Value : null,
                u.Code,
                u.Name,
                u.Type,
                u.ManagerPositionId?.Value,
                u.ManagerPositionCode,
                u.ManagerJobName,
                u.CostCenterCode,
                u.BranchId,
                u.ValidFrom,
                u.ValidTo,
                (u.ValidFrom is null || u.ValidFrom <= today) && (u.ValidTo is null || u.ValidTo >= today),
                numbers.Count,
                numbers.Norm,
                numbers.Filled,
                numbers.Vacant);
        })]);
    }
}

internal sealed class GetOrgUnitsQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : IQueryHandler<GetOrgUnitsQuery, PagedResult<OrgUnitListItem>>
{
    public async Task<Result<PagedResult<OrgUnitListItem>>> Handle(GetOrgUnitsQuery request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var units = scope.FilterUnits(db.OrgUnits.AsNoTracking(), db);
        foreach (var companyId in new[] { request.CompanyId, companyContext.ActiveCompanyId }.OfType<Guid>().Distinct())
        {
            units = units.Where(u => u.CompanyId == companyId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            units = units.Where(u => TextSearch.Matches(u.Code, pattern) || TextSearch.Matches(u.Name, pattern));
        }

        if (request.Type is { } type)
        {
            units = units.Where(u => u.Type == type);
        }

        if (request.ParentId is { } parentId)
        {
            var parent = OrgUnitId.From(parentId);
            units = units.Where(u => u.ParentId == parent);
        }

        var allUnits = db.OrgUnits.AsNoTracking();
        var page = await GetOrgUnitsQuery.Sorts.Apply(units, request.Paging.Sort, GetOrgUnitsQuery.DefaultSort)
            .Select(u => new
            {
                u.Id,
                u.CompanyId,
                u.Code,
                u.Name,
                u.Type,
                u.ParentId,
                ParentName = allUnits.Where(p => p.Id == u.ParentId).Select(p => p.Name).FirstOrDefault(),
                u.ValidFrom,
                u.ValidTo,
            })
            .ToPagedResultAsync(request.Paging, cancellationToken);

        var companies = await OrgReferences.CompaniesAsync(organization, page.Items.Select(u => u.CompanyId), cancellationToken);
        return new PagedResult<OrgUnitListItem>(
            [.. page.Items.Select(u =>
            {
                var company = companies.GetValueOrDefault(u.CompanyId);
                var today = HrClock.Today(timeProvider, company?.TimeZoneId);
                return new OrgUnitListItem(
                    u.Id.Value,
                    u.CompanyId,
                    company?.ShortName ?? string.Empty,
                    u.Code,
                    u.Name,
                    u.Type,
                    u.ParentId?.Value,
                    u.ParentName,
                    (u.ValidFrom is null || u.ValidFrom <= today) && (u.ValidTo is null || u.ValidTo >= today));
            })],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal sealed class GetOrgUnitQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : IQueryHandler<GetOrgUnitQuery, OrgUnitDetails>
{
    public async Task<Result<OrgUnitDetails>> Handle(GetOrgUnitQuery request, CancellationToken cancellationToken)
    {
        var id = OrgUnitId.From(request.Id);
        var scope = await OrgScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var row = await scope.FilterUnits(db.OrgUnits.AsNoTracking(), db)
            .Where(u => u.Id == id)
            .Select(u => new
            {
                Unit = u,
                ParentName = db.OrgUnits.Where(p => p.Id == u.ParentId).Select(p => p.Name).FirstOrDefault(),
                ManagerPositionCode = db.Positions.Where(p => p.Id == u.ManagerPositionId).Select(p => p.Code).FirstOrDefault(),
                ManagerJobName = db.Positions.Where(p => p.Id == u.ManagerPositionId)
                    .Join(db.Jobs, p => p.JobId, j => j.Id, (p, j) => j.Name)
                    .FirstOrDefault(),
                Version = EF.Property<uint>(u, ConcurrencyVersion.PropertyName),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return CommonErrors.NotFound;
        }

        var unit = row.Unit;
        var company = (await organization.GetCompaniesAsync([unit.CompanyId], cancellationToken)).SingleOrDefault();
        var branch = unit.BranchId is { } branchId ? (await organization.GetBranchesAsync([branchId], cancellationToken)).SingleOrDefault() : null;
        return new OrgUnitDetails(
            unit.Id.Value,
            unit.CompanyId,
            company?.ShortName ?? string.Empty,
            unit.ParentId?.Value,
            row.ParentName,
            unit.Code,
            unit.Name,
            unit.Type,
            unit.ManagerPositionId?.Value,
            row.ManagerPositionCode,
            row.ManagerJobName,
            unit.CostCenterCode,
            unit.BranchId,
            branch?.Name,
            unit.ValidFrom,
            unit.ValidTo,
            unit.IsActiveOn(HrClock.Today(timeProvider, company?.TimeZoneId)),
            row.Version,
            unit.CreatedAt,
            unit.ModifiedAt);
    }
}
