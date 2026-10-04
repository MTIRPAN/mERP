using System.Diagnostics;
using System.Globalization;
using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.SharedKernel.Markers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Merp.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes field level changes of <c>[Audited]</c> entities to the module's <c>audit_entries</c> table in the same
/// transaction (D13). Values of <c>[Sensitive]</c> properties are never written; only the fact that they changed.
/// </summary>
public sealed class AuditLogInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public const string MaskedValue = "***";

    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        nameof(IAuditable.CreatedAt),
        nameof(IAuditable.CreatedBy),
        nameof(IAuditable.ModifiedAt),
        nameof(IAuditable.ModifiedBy),
        nameof(ISoftDeletable.DeletedAt),
        nameof(ISoftDeletable.DeletedBy),
        ModuleDbContext.VersionProperty,
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = timeProvider.GetUtcNow();
        var correlationId = Activity.Current?.TraceId.ToString();
        var entries = new List<AuditEntry>();

        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                         && e.Metadata.ClrType.IsDefined(typeof(AuditedAttribute), inherit: true))
                     .ToList())
        {
            var entityType = entry.Metadata.ClrType.Name;
            var entityId = FormatKey(entry);

            AuditEntry Create(AuditAction action, string? field, string? oldValue, string? newValue) => new()
            {
                OccurredAt = now,
                UserId = currentUser.UserId,
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                Field = field,
                OldValue = oldValue,
                NewValue = newValue,
                CorrelationId = correlationId,
            };

            if (entry.State == EntityState.Deleted || IsSoftDeletion(entry))
            {
                entries.Add(Create(AuditAction.Delete, null, null, null));
                continue;
            }

            foreach (var property in entry.Properties.Where(p => !IgnoredProperties.Contains(p.Metadata.Name) && !p.Metadata.IsShadowProperty()))
            {
                var sensitive = IsSensitive(property);
                if (entry.State == EntityState.Added)
                {
                    if (property.CurrentValue is not null)
                    {
                        entries.Add(Create(AuditAction.Insert, property.Metadata.Name, null, Format(property, property.CurrentValue, sensitive)));
                    }
                }
                else if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                {
                    entries.Add(Create(
                        AuditAction.Update,
                        property.Metadata.Name,
                        Format(property, property.OriginalValue, sensitive),
                        Format(property, property.CurrentValue, sensitive)));
                }
            }
        }

        if (entries.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(entries);
        }
    }

    private static bool IsSoftDeletion(EntityEntry entry) =>
        entry is { State: EntityState.Modified, Entity: ISoftDeletable }
        && entry.Property(nameof(ISoftDeletable.IsDeleted)) is { IsModified: true, CurrentValue: true, OriginalValue: false };

    private static bool IsSensitive(PropertyEntry property) =>
        property.Metadata.PropertyInfo?.IsDefined(typeof(SensitiveAttribute), inherit: true) == true;

    private static string FormatKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? string.Empty
            : string.Join(",", key.Properties.Select(p => Format(entry.Property(p.Name), entry.Property(p.Name).CurrentValue, sensitive: false)));
    }

    private static string? Format(PropertyEntry property, object? value, bool sensitive)
    {
        if (value is null)
        {
            return null;
        }

        if (sensitive)
        {
            return MaskedValue;
        }

        // Use the provider representation (e.g. Guid for strongly typed ids, JSON for LocalizedString)
        var converter = property.Metadata.GetTypeMapping().Converter;
        var providerValue = converter is null ? value : converter.ConvertToProvider(value);
        return providerValue switch
        {
            null => null,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => providerValue.ToString(),
        };
    }
}
