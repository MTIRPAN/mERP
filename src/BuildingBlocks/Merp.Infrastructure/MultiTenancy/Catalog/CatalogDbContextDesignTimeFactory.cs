using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Merp.Infrastructure.MultiTenancy.Catalog;

/// <summary>Used by <c>dotnet ef</c> only.</summary>
internal sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, "Host=localhost;Database=merp_catalog_design_time;Username=merp;Password=merp");
        return new CatalogDbContext(options.Options);
    }
}
