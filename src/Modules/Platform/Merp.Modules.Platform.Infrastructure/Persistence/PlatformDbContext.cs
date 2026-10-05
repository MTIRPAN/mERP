using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Locations;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : ModuleDbContext(options), IModuleSchema, IPlatformDbContext
{
    public static string SchemaName => "platform";

    public override string Schema => SchemaName;

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Province> Provinces => Set<Province>();

    public DbSet<District> Districts => Set<District>();
}
