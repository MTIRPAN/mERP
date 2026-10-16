namespace Merp.Modules.HR.Domain.Organization;

/// <summary>
/// One row of the reporting lines closure table (D37): <see cref="ManagerPositionId"/> manages
/// <see cref="SubordinatePositionId"/> directly (depth 1) or through depth − 1 positions in between.
/// </summary>
public sealed class ReportingLine
{
    private ReportingLine()
    {
    }

    public ReportingLine(Guid companyId, PositionId managerPositionId, PositionId subordinatePositionId, int depth)
    {
        if (depth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "A reporting line is at least one level deep.");
        }

        CompanyId = companyId;
        ManagerPositionId = managerPositionId;
        SubordinatePositionId = subordinatePositionId;
        Depth = depth;
    }

    public Guid CompanyId { get; private set; }

    public PositionId ManagerPositionId { get; private set; }

    public PositionId SubordinatePositionId { get; private set; }

    public int Depth { get; private set; }
}

/// <summary>What the chart needs to know about a unit.</summary>
public sealed record OrgChartUnit(OrgUnitId Id, OrgUnitId? ParentId, PositionId? ManagerPositionId);

/// <summary>What the chart needs to know about a position.</summary>
public sealed record OrgChartPosition(PositionId Id, OrgUnitId OrgUnitId, PositionId? ReportsToPositionId, PositionStatus Status);

/// <summary>
/// Who manages whom in one company, from the positions and units (D37):
/// <list type="number">
/// <item>the position the position reports to;</item>
/// <item>otherwise the manager position of its unit, or of the nearest unit above when the position manages its
/// own unit or the unit has no manager;</item>
/// <item>closed positions are skipped: their subordinates move up to the closed position's own manager.</item>
/// </list>
/// Chains stop at a repeated position, so a cycle never loops; commands refuse changes that would create one.
/// </summary>
public sealed class ReportingChart
{
    private readonly Dictionary<OrgUnitId, OrgChartUnit> _units;
    private readonly Dictionary<PositionId, OrgChartPosition> _positions;

    public ReportingChart(IEnumerable<OrgChartUnit> units, IEnumerable<OrgChartPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(positions);
        _units = units.ToDictionary(u => u.Id);
        _positions = positions.ToDictionary(p => p.Id);
    }

    /// <summary>The position that manages <paramref name="positionId"/>; null at the top or for an unknown position.</summary>
    public PositionId? ManagerOf(PositionId positionId)
    {
        if (!_positions.TryGetValue(positionId, out var position))
        {
            return null;
        }

        var seen = new HashSet<PositionId> { positionId };
        var candidate = DirectManagerOf(position);
        while (candidate is { } id && _positions[id].Status == PositionStatus.Closed)
        {
            if (!seen.Add(id))
            {
                return null;
            }

            candidate = DirectManagerOf(_positions[id]);
        }

        return candidate;
    }

    /// <summary>The managers above <paramref name="positionId"/>, nearest first.</summary>
    public IReadOnlyList<PositionId> ChainOf(PositionId positionId)
    {
        var chain = new List<PositionId>();
        var seen = new HashSet<PositionId> { positionId };
        for (var manager = ManagerOf(positionId); manager is { } id && seen.Add(id); manager = ManagerOf(id))
        {
            chain.Add(id);
        }

        return chain;
    }

    /// <summary>A position that ends up managing itself, if any.</summary>
    public PositionId? FindCycle() =>
        _positions.Values
            .Where(p => p.Status != PositionStatus.Closed && IsInCycle(p.Id))
            .Select(p => (PositionId?)p.Id)
            .FirstOrDefault();

    /// <summary>The closure table of the company: one row per open position and each manager above it.</summary>
    public IReadOnlyList<ReportingLine> BuildLines(Guid companyId) =>
        [.. _positions.Values
            .Where(p => p.Status != PositionStatus.Closed)
            .SelectMany(p => ChainOf(p.Id).Select((manager, index) => new ReportingLine(companyId, manager, p.Id, index + 1)))];

    private bool IsInCycle(PositionId positionId)
    {
        var seen = new HashSet<PositionId>();
        for (var manager = ManagerOf(positionId); manager is { } id; manager = ManagerOf(id))
        {
            if (id == positionId)
            {
                return true;
            }

            if (!seen.Add(id))
            {
                return false;
            }
        }

        return false;
    }

    private PositionId? DirectManagerOf(OrgChartPosition position)
    {
        if (position.ReportsToPositionId is { } reportsTo && reportsTo != position.Id && _positions.ContainsKey(reportsTo))
        {
            return reportsTo;
        }

        var visited = new HashSet<OrgUnitId>();
        for (OrgUnitId? unitId = position.OrgUnitId; unitId is { } id && visited.Add(id) && _units.TryGetValue(id, out var unit); unitId = unit.ParentId)
        {
            if (unit.ManagerPositionId is { } manager && manager != position.Id && _positions.ContainsKey(manager))
            {
                return manager;
            }
        }

        return null;
    }
}
