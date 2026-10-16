using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>
/// Set based writes of the organization that EF Core cannot express (ltree paths, replacing a table's rows);
/// Infrastructure runs them as SQL in the command's transaction.
/// </summary>
public interface IOrgStructureStore
{
    /// <summary>
    /// Moves the sub units of a moved unit: paths below <paramref name="oldPath"/> continue below
    /// <paramref name="newPath"/>. The moved unit itself is saved by the unit of work.
    /// </summary>
    Task MoveSubtreeAsync(string oldPath, string newPath, CancellationToken cancellationToken);

    /// <summary>Replaces every reporting line of <paramref name="companyId"/> with <paramref name="lines"/>.</summary>
    Task ReplaceReportingLinesAsync(Guid companyId, IReadOnlyCollection<ReportingLine> lines, CancellationToken cancellationToken);
}

/// <summary>The managers above a position, from the reporting lines (D37).</summary>
public interface IReportingLines
{
    /// <summary>Manager positions above <paramref name="positionId"/>, nearest first.</summary>
    Task<IReadOnlyList<PositionId>> GetManagerChainAsync(PositionId positionId, CancellationToken cancellationToken = default);
}

public sealed class ReportingLineReader(IHrDbContext db) : IReportingLines
{
    public async Task<IReadOnlyList<PositionId>> GetManagerChainAsync(PositionId positionId, CancellationToken cancellationToken = default) =>
        await db.ReportingLines.AsNoTracking()
            .Where(r => r.SubordinatePositionId == positionId)
            .OrderBy(r => r.Depth)
            .Select(r => r.ManagerPositionId)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// The units and positions of a company as the current command sees them: the saved rows with the command's
/// pending changes on top. Domain event handlers run before the unit of work saves (D21).
/// </summary>
internal static class OrgChartLoader
{
    public static async Task<(List<OrgChartUnit> Units, List<OrgChartPosition> Positions)> LoadAsync(
        IHrDbContext db,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var units = (await db.OrgUnits.AsNoTracking()
                .Where(u => u.CompanyId == companyId)
                .Select(u => new OrgChartUnit(u.Id, u.ParentId, u.ManagerPositionId))
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id);
        foreach (var unit in db.OrgUnits.Local.Where(u => u.CompanyId == companyId))
        {
            units[unit.Id] = new OrgChartUnit(unit.Id, unit.ParentId, unit.ManagerPositionId);
        }

        var positions = (await db.Positions.AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .Select(p => new OrgChartPosition(p.Id, p.OrgUnitId, p.ReportsToPositionId, p.Status))
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id);
        foreach (var position in db.Positions.Local.Where(p => p.CompanyId == companyId))
        {
            positions[position.Id] = new OrgChartPosition(position.Id, position.OrgUnitId, position.ReportsToPositionId, position.Status);
        }

        return ([.. units.Values], [.. positions.Values]);
    }

    /// <summary>
    /// The company's chart, including the command's pending changes, makes a position manage itself. Commands check
    /// it after changing a unit or a position and before saving.
    /// </summary>
    public static async Task<bool> HasCycleAsync(IHrDbContext db, Guid companyId, CancellationToken cancellationToken)
    {
        var (units, positions) = await LoadAsync(db, companyId, cancellationToken);
        return new ReportingChart(units, positions).FindCycle() is not null;
    }
}

/// <summary>Rebuilds the closure table of a company from its positions and units (D37).</summary>
public sealed class ReportingLinesBuilder(IHrDbContext db, IOrgStructureStore store)
{
    public async Task RebuildAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var (units, positions) = await OrgChartLoader.LoadAsync(db, companyId, cancellationToken);
        var lines = new ReportingChart(units, positions).BuildLines(companyId);
        await store.ReplaceReportingLinesAsync(companyId, lines, cancellationToken);
    }
}

/// <summary>The sub units of a moved unit follow it, in the same transaction (D37).</summary>
internal sealed class MoveSubUnitsOnOrgUnitMoved(IOrgStructureStore store) : IDomainEventHandler<OrgUnitMoved>
{
    public Task Handle(OrgUnitMoved domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return store.MoveSubtreeAsync(domainEvent.OldPath, domainEvent.NewPath, cancellationToken);
    }
}

internal sealed class RebuildReportingLinesOnStructureChanged(ReportingLinesBuilder builder) : IDomainEventHandler<ReportingStructureChanged>
{
    public Task Handle(ReportingStructureChanged domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return builder.RebuildAsync(domainEvent.CompanyId, cancellationToken);
    }
}

/// <summary>
/// Rebuilds the reporting lines of every company of the tenant (nightly job <c>hr.reporting-lines</c>, D37).
/// Returns the number of companies.
/// </summary>
public sealed record RebuildReportingLinesCommand : ICommand<int>;

internal sealed class RebuildReportingLinesCommandHandler(IHrDbContext db, ReportingLinesBuilder builder)
    : ICommandHandler<RebuildReportingLinesCommand, int>
{
    public async Task<Result<int>> Handle(RebuildReportingLinesCommand request, CancellationToken cancellationToken)
    {
        // Companies with positions, and companies whose stale lines must go
        var companies = await db.Positions.AsNoTracking().Select(p => p.CompanyId)
            .Union(db.ReportingLines.AsNoTracking().Select(r => r.CompanyId))
            .ToListAsync(cancellationToken);
        foreach (var companyId in companies)
        {
            await builder.RebuildAsync(companyId, cancellationToken);
        }

        return companies.Count;
    }
}
