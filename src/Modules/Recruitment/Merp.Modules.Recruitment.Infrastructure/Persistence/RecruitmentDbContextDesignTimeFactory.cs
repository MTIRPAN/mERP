using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c>; at runtime the connection string comes from the current tenant.</summary>
internal sealed class RecruitmentDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RecruitmentDbContext>
{
    public RecruitmentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RecruitmentDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureModuleDbContext<RecruitmentDbContext>(
            options,
            "Host=localhost;Database=merp_design_time;Username=merp;Password=merp");
        return new RecruitmentDbContext(options.Options);
    }
}
