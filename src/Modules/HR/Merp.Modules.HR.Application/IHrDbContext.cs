using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Merp.Modules.HR.Application;

/// <summary>
/// HR data for handlers. Implemented by the module DbContext in Infrastructure; commands only add or change
/// entities, the unit of work saves them (PLAN 0.5).
/// </summary>
public interface IHrDbContext
{
    DbSet<OrgUnit> OrgUnits { get; }

    DbSet<Job> Jobs { get; }

    DbSet<Position> Positions { get; }

    /// <summary>Closure table of the position hierarchy, rebuilt per company (D37).</summary>
    DbSet<ReportingLine> ReportingLines { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
