using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>
/// The units and positions a caller reaches through the scopes of one permission (docs/domain/yetki-ve-onay.md
/// §1.2, D37): Tenant all of them, Company the company's, Branch the units of the branch, OrgUnit the unit and, when
/// the scope includes them, its sub units (ltree). Positions follow their units. ManagedTeam names the positions
/// below the caller's positions (reporting lines) and Self the caller's own positions; they also reach the units of
/// those positions.
/// </summary>
internal sealed class OrgScope
{
    private OrgScope(
        bool tenantWide,
        IReadOnlyList<Guid> companyIds,
        IReadOnlyList<Guid> branchIds,
        IReadOnlyList<OrgUnitId> unitIds,
        IReadOnlyList<OrgUnitId> subtreeRootIds,
        IReadOnlyList<PositionId> positionIds)
    {
        IsTenantWide = tenantWide;
        CompanyIds = companyIds;
        BranchIds = branchIds;
        UnitIds = unitIds;
        SubtreeRootIds = subtreeRootIds;
        PositionIds = positionIds;
    }

    public bool IsTenantWide { get; }

    public IReadOnlyList<Guid> CompanyIds { get; }

    public IReadOnlyList<Guid> BranchIds { get; }

    /// <summary>OrgUnit scopes without their sub units.</summary>
    public IReadOnlyList<OrgUnitId> UnitIds { get; }

    /// <summary>OrgUnit scopes with their sub units.</summary>
    public IReadOnlyList<OrgUnitId> SubtreeRootIds { get; }

    /// <summary>Positions of the ManagedTeam and Self scopes.</summary>
    public IReadOnlyList<PositionId> PositionIds { get; }

    public static async Task<OrgScope> ResolveAsync(
        IAccessScopeProvider scopes,
        string permission,
        IHrDbContext db,
        IPositionHolders holders,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var list = await scopes.GetCurrentUserScopesAsync(permission, cancellationToken);
        if (list.Any(s => s.Type == AccessScopeTypes.Tenant))
        {
            return new OrgScope(true, [], [], [], [], []);
        }

        var positionIds = new List<PositionId>();
        var managedTeam = list.Any(s => s.Type == AccessScopeTypes.ManagedTeam);
        var self = list.Any(s => s.Type == AccessScopeTypes.Self);
        if ((managedTeam || self) && currentUser.UserId is { } userId)
        {
            var own = (await holders.GetPositionsOfUserAsync(userId, cancellationToken)).Select(h => h.PositionId).Distinct().ToList();
            if (self)
            {
                positionIds.AddRange(own);
            }

            if (managedTeam && own.Count > 0)
            {
                positionIds.AddRange(await db.ReportingLines.AsNoTracking()
                    .Where(r => own.Contains(r.ManagerPositionId))
                    .Select(r => r.SubordinatePositionId)
                    .Distinct()
                    .ToListAsync(cancellationToken));
            }
        }

        var units = list.Where(s => s.Type == AccessScopeTypes.OrgUnit && s.ScopeId is not null).ToList();
        return new OrgScope(
            false,
            IdsOf(list, AccessScopeTypes.Company),
            IdsOf(list, AccessScopeTypes.Branch),
            [.. units.Where(s => !s.IncludeSubUnits).Select(s => OrgUnitId.From(s.ScopeId!.Value)).Distinct()],
            [.. units.Where(s => s.IncludeSubUnits).Select(s => OrgUnitId.From(s.ScopeId!.Value)).Distinct()],
            [.. positionIds.Distinct()]);
    }

    /// <summary>Only the Tenant and Company scopes, for commands that manage records of a company (D37).</summary>
    public static async Task<OrgScope> ForCompaniesAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken) =>
        ForCompanies(await scopes.GetCurrentUserScopesAsync(permission, cancellationToken));

    /// <summary>The Tenant and Company scopes of <paramref name="list"/>, e.g. of an importing user (D40).</summary>
    public static OrgScope ForCompanies(IReadOnlyList<AccessScope> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new OrgScope(list.Any(s => s.Type == AccessScopeTypes.Tenant), IdsOf(list, AccessScopeTypes.Company), [], [], [], []);
    }

    /// <summary>
    /// Units and positions are managed per company (D37): the Tenant scope or the company's scope.
    /// </summary>
    public bool CoversCompany(Guid companyId) => IsTenantWide || CompanyIds.Contains(companyId);

    public IQueryable<OrgUnit> FilterUnits(IQueryable<OrgUnit> units, IHrDbContext db)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(db);
        if (IsTenantWide)
        {
            return units;
        }

        var companyIds = CompanyIds;
        var branchIds = BranchIds;
        var unitIds = UnitIds;
        var rootIds = SubtreeRootIds;
        var positionIds = PositionIds;
        var allUnits = db.OrgUnits;
        var positions = db.Positions;
        return units.Where(u =>
            companyIds.Contains(u.CompanyId)
            || (u.BranchId != null && branchIds.Contains(u.BranchId.Value))
            || unitIds.Contains(u.Id)
            || allUnits.Any(root => rootIds.Contains(root.Id) && OrgPaths.IsWithin(u.Path, root.Path))
            || positions.Any(p => positionIds.Contains(p.Id) && p.OrgUnitId == u.Id));
    }

    public IQueryable<Position> FilterPositions(IQueryable<Position> positions, IHrDbContext db)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(db);
        if (IsTenantWide)
        {
            return positions;
        }

        var companyIds = CompanyIds;
        var branchIds = BranchIds;
        var unitIds = UnitIds;
        var rootIds = SubtreeRootIds;
        var positionIds = PositionIds;
        var units = db.OrgUnits;
        return positions.Where(p =>
            companyIds.Contains(p.CompanyId)
            || positionIds.Contains(p.Id)
            || units.Any(u => u.Id == p.OrgUnitId
                && ((u.BranchId != null && branchIds.Contains(u.BranchId.Value))
                    || unitIds.Contains(u.Id)
                    || units.Any(root => rootIds.Contains(root.Id) && OrgPaths.IsWithin(u.Path, root.Path)))));
    }

    private static List<Guid> IdsOf(IEnumerable<AccessScope> scopes, string type) =>
        [.. scopes.Where(s => s.Type == type && s.ScopeId is not null).Select(s => s.ScopeId!.Value).Distinct()];
}
