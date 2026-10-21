using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// The employees a caller reaches through the scopes of one permission (D38): Tenant all of them; Company the
/// employments of the company; Branch and OrgUnit the employments whose assignment is in the branch or the unit (sub
/// units with ltree when the scope includes them); ManagedTeam the holders of the positions below the caller's
/// positions and the people whose manager override names one of the caller's positions; Self the caller's own record.
/// </summary>
internal sealed class EmployeeScope
{
    private static readonly EmployeeScope Nothing = new(false, [], [], [], [], [], [], null, null);

    private EmployeeScope(
        bool tenantWide,
        IReadOnlyList<Guid> companyIds,
        IReadOnlyList<Guid> branchIds,
        IReadOnlyList<OrgUnitId> unitIds,
        IReadOnlyList<OrgUnitId> subtreeRootIds,
        IReadOnlyList<PositionId> teamPositionIds,
        IReadOnlyList<PositionId> ownPositionIds,
        Guid? selfEmployeeId,
        Guid? selfUserId)
    {
        IsTenantWide = tenantWide;
        CompanyIds = companyIds;
        BranchIds = branchIds;
        UnitIds = unitIds;
        SubtreeRootIds = subtreeRootIds;
        TeamPositionIds = teamPositionIds;
        OwnPositionIds = ownPositionIds;
        SelfEmployeeId = selfEmployeeId;
        SelfUserId = selfUserId;
    }

    public bool IsTenantWide { get; }

    public IReadOnlyList<Guid> CompanyIds { get; }

    public IReadOnlyList<Guid> BranchIds { get; }

    public IReadOnlyList<OrgUnitId> UnitIds { get; }

    public IReadOnlyList<OrgUnitId> SubtreeRootIds { get; }

    /// <summary>Positions below the caller's positions (ManagedTeam).</summary>
    public IReadOnlyList<PositionId> TeamPositionIds { get; }

    /// <summary>The caller's positions, which manager overrides may name (ManagedTeam).</summary>
    public IReadOnlyList<PositionId> OwnPositionIds { get; }

    public Guid? SelfEmployeeId { get; }

    public Guid? SelfUserId { get; }

    public static async Task<EmployeeScope> ResolveAsync(
        IAccessScopeProvider scopes,
        string permission,
        IHrDbContext db,
        IPositionHolders holders,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var list = await scopes.GetCurrentUserScopesAsync(permission, cancellationToken);
        if (list.Count == 0)
        {
            return Nothing;
        }

        if (list.Any(s => s.Type == AccessScopeTypes.Tenant))
        {
            return new EmployeeScope(true, [], [], [], [], [], [], null, null);
        }

        List<PositionId> team = [];
        List<PositionId> own = [];
        if (list.Any(s => s.Type == AccessScopeTypes.ManagedTeam) && currentUser.UserId is { } userId)
        {
            own = [.. (await holders.GetPositionsOfUserAsync(userId, cancellationToken)).Select(h => h.PositionId).Distinct()];
            if (own.Count > 0)
            {
                team = await db.ReportingLines.AsNoTracking()
                    .Where(r => own.Contains(r.ManagerPositionId))
                    .Select(r => r.SubordinatePositionId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }
        }

        var self = list.Any(s => s.Type == AccessScopeTypes.Self);
        var units = list.Where(s => s.Type == AccessScopeTypes.OrgUnit && s.ScopeId is not null).ToList();
        return new EmployeeScope(
            false,
            IdsOf(list, AccessScopeTypes.Company),
            IdsOf(list, AccessScopeTypes.Branch),
            [.. units.Where(s => !s.IncludeSubUnits).Select(s => OrgUnitId.From(s.ScopeId!.Value)).Distinct()],
            [.. units.Where(s => s.IncludeSubUnits).Select(s => OrgUnitId.From(s.ScopeId!.Value)).Distinct()],
            team,
            own,
            self ? currentUser.EmployeeId : null,
            self ? currentUser.UserId : null);
    }

    public IQueryable<EmploymentRow> Filter(IQueryable<EmploymentRow> rows, IHrDbContext db)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(db);
        if (IsTenantWide)
        {
            return rows;
        }

        var companyIds = CompanyIds;
        var branchIds = BranchIds;
        var unitIds = UnitIds;
        var rootIds = SubtreeRootIds;
        var teamIds = TeamPositionIds;
        var ownIds = OwnPositionIds;
        var selfEmployeeId = SelfEmployeeId is { } employeeId ? EmployeeId.From(employeeId) : (EmployeeId?)null;
        var selfUserId = SelfUserId;
        var positions = db.Positions;
        var units = db.OrgUnits;
        return rows.Where(r =>
            companyIds.Contains(r.CompanyId)
            || (r.BranchId != null && branchIds.Contains(r.BranchId.Value))
            || positions.Any(p => p.Id == r.PositionId
                && (unitIds.Contains(p.OrgUnitId)
                    || units.Any(u => u.Id == p.OrgUnitId && units.Any(root => rootIds.Contains(root.Id) && OrgPaths.IsWithin(u.Path, root.Path)))))
            || (r.PositionId != null && teamIds.Contains(r.PositionId.Value))
            || (r.ManagerPositionId != null && ownIds.Contains(r.ManagerPositionId.Value))
            || (selfEmployeeId != null && r.EmployeeId == selfEmployeeId)
            || (selfUserId != null && r.UserId == selfUserId));
    }

    private static List<Guid> IdsOf(IReadOnlyList<AccessScope> scopes, string type) =>
        [.. scopes.Where(s => s.Type == type && s.ScopeId is not null).Select(s => s.ScopeId!.Value).Distinct()];
}
