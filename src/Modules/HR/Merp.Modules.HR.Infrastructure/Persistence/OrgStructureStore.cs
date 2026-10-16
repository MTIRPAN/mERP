using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Infrastructure.Persistence;

/// <summary>
/// <see cref="IOrgStructureStore"/> in SQL, inside the command's transaction (D37). Domain event handlers call it
/// before the unit of work saves the changed aggregate.
/// </summary>
internal sealed class OrgStructureStore(HrDbContext db) : IOrgStructureStore
{
    public async Task MoveSubtreeAsync(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldPath);
        ArgumentException.ThrowIfNullOrEmpty(newPath);

        // The moved unit keeps its old path in the table until the unit of work saves it, so it is left out here
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE hr.org_units
            SET path = {newPath}::ltree || subpath(path, nlevel({oldPath}::ltree))
            WHERE path <@ {oldPath}::ltree AND path <> {oldPath}::ltree
            """,
            cancellationToken);
    }

    public async Task ReplaceReportingLinesAsync(Guid companyId, IReadOnlyCollection<ReportingLine> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        await db.Database.ExecuteSqlAsync($"DELETE FROM hr.reporting_lines WHERE company_id = {companyId}", cancellationToken);
        if (lines.Count == 0)
        {
            return;
        }

        var managers = lines.Select(l => l.ManagerPositionId.Value).ToArray();
        var subordinates = lines.Select(l => l.SubordinatePositionId.Value).ToArray();
        var depths = lines.Select(l => l.Depth).ToArray();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO hr.reporting_lines (manager_position_id, subordinate_position_id, company_id, depth)
            SELECT line.manager, line.subordinate, {companyId}, line.depth
            FROM unnest({managers}::uuid[], {subordinates}::uuid[], {depths}::integer[]) AS line(manager, subordinate, depth)
            """,
            cancellationToken);
    }
}
