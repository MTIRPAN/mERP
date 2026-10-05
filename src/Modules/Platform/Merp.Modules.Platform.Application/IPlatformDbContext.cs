using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Merp.Modules.Platform.Application;

/// <summary>
/// Platform data for handlers. Implemented by the module DbContext in Infrastructure; commands only add or change
/// entities, the unit of work saves them (PLAN 0.5).
/// </summary>
public interface IPlatformDbContext
{
    DbSet<Company> Companies { get; }

    DbSet<Branch> Branches { get; }

    DbSet<Province> Provinces { get; }

    DbSet<District> Districts { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
