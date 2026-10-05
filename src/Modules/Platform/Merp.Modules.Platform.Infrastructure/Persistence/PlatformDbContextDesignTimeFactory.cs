using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Merp.Modules.Platform.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c>; at runtime the connection string comes from the current tenant.</summary>
internal sealed class PlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureModuleDbContext<PlatformDbContext>(
            options,
            "Host=localhost;Database=merp_design_time;Username=merp;Password=merp");
        return new PlatformDbContext(options.Options);
    }
}
