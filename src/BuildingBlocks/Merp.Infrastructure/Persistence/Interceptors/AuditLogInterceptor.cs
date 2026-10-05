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
        var inserted = new HashSet<(string Type, string Id)>();

        AuditEntry Write(string entityType, string entityId, AuditAction action, string? field, string? oldValue, string? newValue) => new()
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

        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                         && e.Metadata.ClrType.IsDefined(typeof(AuditedAttribute), inherit: true))
                     .ToList())
        {
            var entityType = entry.Metadata.ClrType.Name;
            var entityId = FormatKey(entry);
            if (entry.State == EntityState.Added)
            {
                inserted.Add((entityType, entityId));
            }

            AuditEntry Create(AuditAction action, string? field, string? oldValue, string? newValue) =>
                Write(entityType, entityId, action, field, oldValue, newValue);

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

        entries.AddRange(OwnedChanges(context, inserted, Write));

        if (entries.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(entries);
        }
    }

    /// <summary>
    /// Value objects mapped as owned types (an address) are audited as fields of their <c>[Audited]</c> owner, e.g.
    /// <c>Address.City</c>. Replacing the value object shows up as a deleted and an added entry with the owner's
    /// key; the pair is compared field by field.
    /// </summary>
    private static IEnumerable<AuditEntry> OwnedChanges(
        DbContext context,
        HashSet<(string Type, string Id)> inserted,
        Func<string, string, AuditAction, string?, string?, string?, AuditEntry> write)
    {
        var groups = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && e.Metadata.FindOwnership() is { PrincipalToDependent: not null } ownership
                && ownership.PrincipalEntityType.ClrType.IsDefined(typeof(AuditedAttribute), inherit: true))
            .GroupBy(e =>
            {
                var ownership = e.Metadata.FindOwnership()!;
                return (Owner: ownership.PrincipalEntityType.ClrType.Name, Navigation: ownership.PrincipalToDependent!.Name, Key: FormatKey(e));
            })
            .ToList();

        foreach (var group in groups)
        {
            var before = group.FirstOrDefault(e => e.State is EntityState.Deleted or EntityState.Modified);
            var after = group.FirstOrDefault(e => e.State is EntityState.Added or EntityState.Modified);
            var action = before is null && inserted.Contains((group.Key.Owner, group.Key.Key)) ? AuditAction.Insert : AuditAction.Update;

            foreach (var property in (after ?? before)!.Metadata.GetProperties().Where(p => !p.IsShadowProperty()))
            {
                var oldValue = before?.Property(property.Name).OriginalValue;
                var newValue = after?.Property(property.Name).CurrentValue;
                if (Equals(oldValue, newValue))
                {
                    continue;
                }

                var entry = (after ?? before)!.Property(property.Name);
                var sensitive = IsSensitive(entry);
                yield return write(
                    group.Key.Owner,
                    group.Key.Key,
                    action,
                    $"{group.Key.Navigation}.{property.Name}",
                    Format(entry, oldValue, sensitive),
                    Format(entry, newValue, sensitive));
            }
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
