using Merp.Modules.HR.Contracts.Organization;
using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Organization;

/// <summary><see cref="IOrgQuery"/> over the HR tables (D37).</summary>
public sealed class OrgQuery(IHrDbContext db, IPositionHolders holders, IReportingLines reportingLines) : IOrgQuery
{
    public async Task<IReadOnlyList<Guid>> GetSubtreeUnitIdsAsync(Guid orgUnitId, CancellationToken cancellationToken = default)
    {
        var rootId = OrgUnitId.From(orgUnitId);
        var units = db.OrgUnits.AsNoTracking();
        return await units
            .Where(u => units.Any(root => root.Id == rootId && OrgPaths.IsWithin(u.Path, root.Path)))
            .Select(u => u.Id.Value)
            .ToListAsync(cancellationToken);
    }

    public async Task<OrgUnitManager?> GetUnitManagerAsync(Guid orgUnitId, CancellationToken cancellationToken = default)
    {
        if (await UnitManagers.FindAsync(db, OrgUnitId.From(orgUnitId), null, cancellationToken) is not { } manager)
        {
            return null;
        }

        var people = await holders.GetHoldersAsync([manager.PositionId], cancellationToken);
        return new OrgUnitManager(manager.UnitId.Value, manager.PositionId.Value, People(people));
    }

    public async Task<IReadOnlyList<ManagerChainEntry>> GetManagerChainAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (await holders.FindPrimaryAsync(employeeId, null, cancellationToken) is not { } own)
        {
            return [];
        }

        var chain = await reportingLines.GetManagerChainAsync(own.PositionId, cancellationToken);
        if (chain.Count == 0)
        {
            return [];
        }

        var units = await db.Positions.AsNoTracking()
            .Where(p => chain.Contains(p.Id))
            .Select(p => new { p.Id, p.OrgUnitId })
            .ToDictionaryAsync(p => p.Id, p => p.OrgUnitId, cancellationToken);
        var people = await holders.GetHoldersAsync(chain, cancellationToken);
        return [.. chain
            .Select((positionId, index) => (PositionId: positionId, Depth: index + 1))
            .Where(m => units.ContainsKey(m.PositionId))
            .Select(m => new ManagerChainEntry(
                m.Depth,
                m.PositionId.Value,
                units[m.PositionId].Value,
                People(people.Where(h => h.PositionId == m.PositionId))))];
    }

    public async Task<UserOrgInfo?> GetUserOrgInfoAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var held = await holders.GetPositionsOfUserAsync(userId, cancellationToken);
        if (held.Count == 0)
        {
            return null;
        }

        var ids = held.Select(h => h.PositionId).ToList();
        var positions = await db.Positions.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.OrgUnitId, p.CompanyId, p.JobId })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        return new UserOrgInfo(
            userId,
            held[0].EmployeeId,
            [.. held
                .Where(h => positions.ContainsKey(h.PositionId))
                .OrderByDescending(h => h.IsPrimary)
                .Select(h =>
                {
                    var position = positions[h.PositionId];
                    return new UserOrgPosition(h.PositionId.Value, position.OrgUnitId.Value, position.CompanyId, position.JobId.Value, h.IsPrimary);
                })]);
    }

    private static List<OrgPerson> People(IEnumerable<PositionHolder> holders) =>
        [.. holders.DistinctBy(h => h.EmployeeId).Select(h => new OrgPerson(h.EmployeeId, h.UserId))];
}

/// <summary>Manager positions of units: a unit without one is managed by the nearest unit above it that has one.</summary>
internal static class UnitManagers
{
    /// <summary>
    /// The manager position of <paramref name="unitId"/> or of the nearest unit above it, skipping
    /// <paramref name="skip"/>: a position is not its own manager, so the manager of a unit's manager comes from
    /// above (D37).
    /// </summary>
    public static async Task<(OrgUnitId UnitId, PositionId PositionId)?> FindAsync(
        IHrDbContext db,
        OrgUnitId unitId,
        PositionId? skip,
        CancellationToken cancellationToken)
    {
        var path = await db.OrgUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.Path).SingleOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            return null;
        }

        // Labels are the ids from the top unit down to this one
        var lineage = path.Split('.').Select(label => OrgUnitId.From(Guid.ParseExact(label, "N"))).ToList();
        var managers = await db.OrgUnits.AsNoTracking()
            .Where(u => lineage.Contains(u.Id) && u.ManagerPositionId != null)
            .Select(u => new { u.Id, u.ManagerPositionId })
            .ToDictionaryAsync(u => u.Id, u => u.ManagerPositionId!.Value, cancellationToken);
        for (var index = lineage.Count - 1; index >= 0; index--)
        {
            if (managers.TryGetValue(lineage[index], out var manager) && manager != skip)
            {
                return (lineage[index], manager);
            }
        }

        return null;
    }
}
