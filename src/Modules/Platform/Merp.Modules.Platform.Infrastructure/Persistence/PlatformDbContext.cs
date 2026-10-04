using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : ModuleDbContext(options), IModuleSchema
{
    public static string SchemaName => "platform";

    public override string Schema => SchemaName;
}
