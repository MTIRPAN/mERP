using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

namespace Merp.Modules.Platform.Domain.Settings;

public readonly record struct SettingsProfileId(Guid Value) : IEntityId<SettingsProfileId>
{
    public static SettingsProfileId From(Guid value) => new(value);
}

/// <summary>
/// The setting values of the tenant (no company) or of one company (D35): key → value as JSON. One profile per
/// level, so saving several settings at once changes one aggregate. The application checks keys and values against
/// the modules' definitions.
/// </summary>
[Audited]
public sealed class SettingsProfile : AuditableAggregateRoot<SettingsProfileId>
{
    private SettingsProfile()
    {
    }

    /// <summary>Null for the tenant's values.</summary>
    public Guid? CompanyId { get; private set; }

    /// <summary>Setting key → value as JSON text, e.g. <c>platform.notifications.email_enabled</c> → <c>false</c>.</summary>
    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    public static SettingsProfile Create(Guid? companyId) => new()
    {
        Id = EntityId.New<SettingsProfileId>(),
        CompanyId = companyId,
    };

    /// <summary>Sets the given values; a null value removes the key, so it falls back to the level above.</summary>
    public void Apply(IReadOnlyDictionary<string, string?> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var values = new Dictionary<string, string>(Values, StringComparer.Ordinal);
        foreach (var (key, value) in changes)
        {
            if (value is null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value;
            }
        }

        Values = values;
    }
}
