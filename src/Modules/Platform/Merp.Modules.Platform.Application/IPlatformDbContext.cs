using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Merp.Modules.Platform.Application;

/// <summary>
/// The Platform DbContext as handlers see it (D25). Handlers never save: the unit of work commits commands, and
/// queries project with <c>Select</c> and <c>AsNoTracking</c>.
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
