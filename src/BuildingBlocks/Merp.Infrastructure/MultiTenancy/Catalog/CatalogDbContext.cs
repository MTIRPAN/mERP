using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Merp.Infrastructure.MultiTenancy.Catalog;

/// <summary>
/// The catalog database (<c>merp_catalog</c>, D3): tenants and the Data Protection key ring. Hangfire keeps its own
/// tables in the same database.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "catalog";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
            .UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tenant>(builder =>
        {
            builder.ToTable("tenants");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).ValueGeneratedNever();
            builder.Property(t => t.Code).HasMaxLength(32);
            builder.HasIndex(t => t.Code).IsUnique();
            builder.Property(t => t.Name).HasMaxLength(200);
            builder.Property(t => t.ProtectedConnectionString).HasColumnName("connection_string");
            builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(t => t.DefaultCulture).HasMaxLength(20);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}
