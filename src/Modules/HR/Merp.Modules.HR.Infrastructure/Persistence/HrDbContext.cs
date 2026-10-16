using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Infrastructure.Persistence;

public sealed class HrDbContext(DbContextOptions<HrDbContext> options) : ModuleDbContext(options), IModuleSchema, IHrDbContext
{
    public static string SchemaName => "hr";

    public override string Schema => SchemaName;

    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<ReportingLine> ReportingLines => Set<ReportingLine>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // Unit paths are ltree: OrgPaths.IsWithin in LINQ becomes the indexed <@ operator (D37)
        optionsBuilder.AddInterceptors(OrgPathQueryInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");
        base.OnModelCreating(modelBuilder);
    }
}
