using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Merp.Modules.HR.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c>; at runtime the connection string comes from the current tenant.</summary>
internal sealed class HrDbContextDesignTimeFactory : IDesignTimeDbContextFactory<HrDbContext>
{
    public HrDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HrDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureModuleDbContext<HrDbContext>(
            options,
            "Host=localhost;Database=merp_design_time;Username=merp;Password=merp");
        return new HrDbContext(options.Options);
    }
}
