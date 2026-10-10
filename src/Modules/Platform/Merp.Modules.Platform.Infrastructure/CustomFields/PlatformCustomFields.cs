using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.CustomFields;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.Modules.Platform.Domain.CustomFields;
using Merp.Modules.Platform.Domain.Users;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.SharedKernel.CustomFields;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Caching.Hybrid;

namespace Merp.Modules.Platform.Infrastructure.CustomFields;

internal sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("custom_field_definitions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.EntityType).HasMaxLength(CustomFieldDefinition.EntityTypeMaxLength);
        builder.Property(d => d.Code).HasMaxLength(CustomFieldDefinition.CodeMaxLength);
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Options)
            .HasConversion(
                options => JsonSerializer.Serialize(options, Json),
                json => JsonSerializer.Deserialize<List<CustomFieldOption>>(json, Json) ?? new List<CustomFieldOption>(),
                new ValueComparer<IReadOnlyList<CustomFieldOption>>(
                    (left, right) => JsonSerializer.Serialize(left, Json) == JsonSerializer.Serialize(right, Json),
                    value => JsonSerializer.Serialize(value, Json).GetHashCode(StringComparison.Ordinal),
                    value => value.ToList()))
            .HasColumnType("jsonb");
        builder.Ignore(d => d.Rule);

        // A record's values are keyed by code, so a code is unique per entity type whatever the company
        builder.HasIndex(d => new { d.EntityType, d.Code }).IsUnique();
    }
}

/// <summary>The definitions a validator needs, cached per tenant as one entry.</summary>
[ImmutableObject(true)]
public sealed record CustomFieldSnapshot(IReadOnlyList<CustomFieldSnapshot.Field> Fields)
{
    public sealed record Field(string EntityType, string Code, Guid? CompanyId, bool IsActive, CustomFieldRule Rule);
}

internal sealed class CustomFieldCache(HybridCache cache, ITenantContext tenant) : ICustomFieldCache
{
    public static string Key(Guid tenantId) => $"merp:{tenantId}:custom-fields";

    public static string Tag(Guid tenantId) => $"{tenantId}:custom-fields";

    public async Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(Tag(tenant.RequiredTenant.Id), cancellationToken);
}

/// <summary>
/// Checks custom field input against the cached definitions (D12, D30). User fields must name an existing user;
/// employee fields are checked for form only until the HR module can answer.
/// </summary>
internal sealed class PlatformCustomFieldValidator(
    PlatformDbContext db,
    HybridCache cache,
    ITenantContext tenant,
    IEnumerable<ICustomFieldEntityProvider> entities) : ICustomFieldValidator
{
    public const string UnknownFieldCode = "common.errors.unknown_custom_field";
    public const string UnknownUserCode = "platform.custom_fields.errors.unknown_user";

    private static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    public async Task<Result<CustomFieldValues>> ValidateAsync(
        string entityType,
        Guid? companyId,
        IReadOnlyDictionary<string, JsonElement>? input,
        CustomFieldValues current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!entities.SelectMany(p => p.GetEntityTypes()).Any(e => e.Code == entityType))
        {
            throw new InvalidOperationException($"{entityType} is not a registered custom field entity type.");
        }

        var snapshot = await LoadAsync(cancellationToken);
        var active = snapshot.Fields
            .Where(f => f.EntityType == entityType && f.IsActive && (f.CompanyId is null || f.CompanyId == companyId))
            .ToDictionary(f => f.Code, StringComparer.Ordinal);

        var errors = new List<FieldError>();
        foreach (var code in (input ?? new Dictionary<string, JsonElement>()).Keys.Where(code => !active.ContainsKey(code)))
        {
            errors.Add(new FieldError($"customFields.{code}", UnknownFieldCode));
        }

        // Values of fields that are switched off (or belong to another company) stay as they are
        var values = current.Values.Where(v => !active.ContainsKey(v.Key)).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        var users = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var field in active.Values)
        {
            JsonElement? given = input is not null && input.TryGetValue(field.Code, out var value) ? value : null;
            var normalized = field.Rule.Normalize(given);
            if (normalized.IsFailure)
            {
                errors.Add(new FieldError($"customFields.{field.Code}", normalized.Error.Code, normalized.Error.Args));
            }
            else if (normalized.Value is { } stored)
            {
                values[field.Code] = stored;
                if (field.Rule.Type == CustomFieldType.User)
                {
                    users[field.Code] = Guid.Parse(stored.GetString()!);
                }
            }
        }

        if (users.Count > 0)
        {
            var ids = users.Values.Distinct().Select(UserId.From).ToList();
            var existing = (await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken))
                .Select(id => id.Value).ToHashSet();
            errors.AddRange(users.Where(u => !existing.Contains(u.Value)).Select(u => new FieldError($"customFields.{u.Key}", UnknownUserCode)));
        }

        return errors.Count > 0 ? Error.Validation(errors) : CustomFieldValues.From(values);
    }

    private async Task<CustomFieldSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequiredTenant.Id;
        return await cache.GetOrCreateAsync(
            CustomFieldCache.Key(tenantId),
            db,
            static async (context, ct) => new CustomFieldSnapshot([.. (await context.CustomFieldDefinitions.AsNoTracking().ToListAsync(ct))
                .Select(d => new CustomFieldSnapshot.Field(d.EntityType, d.Code, d.CompanyId, d.IsActive, d.Rule))]),
            new HybridCacheEntryOptions { Expiration = Duration, LocalCacheExpiration = Duration },
            [CustomFieldCache.Tag(tenantId)],
            cancellationToken);
    }
}
