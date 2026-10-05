using System.Linq.Expressions;
using System.Reflection;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.Infrastructure.Persistence.Conventions;
using Merp.Infrastructure.Persistence.Inbox;
using Merp.Infrastructure.Persistence.Outbox;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Infrastructure.Persistence;

/// <summary>
/// Base DbContext of a module. Every module owns one schema (CLAUDE.md rule 2) and gets the shared
/// <c>outbox_messages</c>, <c>inbox_messages</c> and <c>audit_entries</c> tables, strongly typed id and jsonb
/// conventions, <c>xmin</c> optimistic concurrency and the soft delete filter (D9).
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Shadow property mapped to PostgreSQL's <c>xmin</c> system column.</summary>
    public const string VersionProperty = "Version";

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>The module schema, e.g. <c>platform</c>.</summary>
    public abstract string Schema { get; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        foreach (var idType in FindStronglyTypedIds())
        {
            configurationBuilder.Properties(idType).HaveConversion(typeof(StronglyTypedIdConverter<>).MakeGenericType(idType));
        }

        configurationBuilder.Properties<LocalizedString>()
            .HaveConversion<LocalizedStringConverter, LocalizedStringComparer>()
            .HaveColumnType("jsonb");

        configurationBuilder.Properties<CustomFieldValues>()
            .HaveConversion<CustomFieldValuesConverter, CustomFieldValuesComparer>()
            .HaveColumnType("jsonb");

        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        ConfigureInfrastructureTables(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => !e.IsOwned() && e.ClrType != typeof(Dictionary<string, object>)))
        {
            var clrType = entityType.ClrType;
            var builder = modelBuilder.Entity(clrType);

            if (clrType.Assembly != typeof(ModuleDbContext).Assembly)
            {
                builder.Property<uint>(VersionProperty).IsRowVersion();
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType) && entityType.BaseType is null)
            {
                builder.HasQueryFilter(SoftDeleteFilter, NotDeleted(clrType));
                builder.HasIndex(nameof(ISoftDeletable.IsDeleted));
            }

            if (typeof(IHasCustomFields).IsAssignableFrom(clrType))
            {
                builder.Property(nameof(IHasCustomFields.CustomFields)).HasColumnName("custom_fields");
            }
        }
    }

    private static void ConfigureInfrastructureTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
            builder.Property(m => m.Type).HasMaxLength(500);
            builder.Property(m => m.Payload).HasColumnType("jsonb");
            builder.Property(m => m.TraceParent).HasMaxLength(100);
                        builder.HasIndex(m => m.OccurredAt)
                .HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL")
                .HasDatabaseName("ix_outbox_messages_pending");
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages");
            builder.HasKey(m => new { m.MessageId, m.Handler });
            builder.Property(m => m.Handler).HasMaxLength(500);
            builder.Property(m => m.Type).HasMaxLength(500);
            builder.Property(m => m.Payload).HasColumnType("jsonb");
            builder.Property(m => m.TraceParent).HasMaxLength(100);
                        builder.HasIndex(m => m.ReceivedAt)
                .HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL")
                .HasDatabaseName("ix_inbox_messages_pending");
        });

        modelBuilder.Entity<AuditEntry>(builder =>
        {
            builder.ToTable("audit_entries");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id).ValueGeneratedNever();
            builder.Property(e => e.EntityType).HasMaxLength(200);
            builder.Property(e => e.EntityId).HasMaxLength(200);
            builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
            builder.Property(e => e.Field).HasMaxLength(200);
            builder.Property(e => e.CorrelationId).HasMaxLength(100);
            builder.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAt });
            builder.HasIndex(e => new { e.UserId, e.OccurredAt });
        });
    }

    private static LambdaExpression NotDeleted(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var isDeleted = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
        return Expression.Lambda(Expression.Not(isDeleted), parameter);
    }

    /// <summary>
    /// Strongly typed ids declared in the module's assemblies: the DbContext assembly and the Merp assemblies it
    /// references directly (domain, application).
    /// </summary>
    private IEnumerable<Type> FindStronglyTypedIds()
    {
        var contextAssembly = GetType().Assembly;
        var assemblies = contextAssembly.GetReferencedAssemblies()
            .Where(name => name.Name?.StartsWith("Merp.", StringComparison.Ordinal) == true)
            .Select(Assembly.Load)
            .Append(contextAssembly)
            .Distinct();

        return assemblies
            .SelectMany(GetLoadableTypes)
            .Where(type => type is { IsValueType: true, IsGenericTypeDefinition: false }
                && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityId<>) && i.GetGenericArguments()[0] == type))
            .Distinct();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
