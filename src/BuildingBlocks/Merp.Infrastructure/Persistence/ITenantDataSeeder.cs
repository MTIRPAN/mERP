namespace Merp.Infrastructure.Persistence;

/// <summary>
/// Data a module keeps in every tenant database (permission catalog, system roles). Runs after the migrations of
/// all modules, in a scope bound to the tenant as a system caller, so the migrator, the development startup and the
/// tests seed the same way. Seeders must be idempotent.
/// </summary>
public interface ITenantDataSeeder
{
    Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}
