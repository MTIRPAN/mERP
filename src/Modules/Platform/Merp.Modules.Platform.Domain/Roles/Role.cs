using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Roles;

public readonly record struct RoleId(Guid Value) : IEntityId<RoleId>
{
    public static RoleId From(Guid value) => new(value);
}

/// <summary>
/// A tenant's set of permissions (docs/domain/yetki-ve-onay.md §1.1). System roles are seeded, keep their code and
/// cannot be deleted; their permissions can be edited and reset to the defaults.
/// </summary>
[Audited]
public sealed class Role : AuditableAggregateRoot<RoleId>, ISoftDeletable
{
    public const int CodeMaxLength = 50;
    public const int DescriptionMaxLength = 500;

    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public static Role Create(Code code, LocalizedString name, string? description, IEnumerable<string> permissions, bool isSystem = false)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A role code has at most {CodeMaxLength} characters.", nameof(code));
        }

        var role = new Role { Id = EntityId.New<RoleId>(), Code = code.Value, IsSystem = isSystem };
        role.Rename(name, description);
        role.SetPermissions(permissions);
        return role;
    }

    public void Rename(LocalizedString name, string? description)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = Text.Optional(description, DescriptionMaxLength, nameof(description));
    }

    /// <summary>Replaces the permissions; codes are compared ordinally and duplicates are ignored.</summary>
    /// <returns>True when the set changed.</returns>
    public bool SetPermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var wanted = permissions.Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet(StringComparer.Ordinal);
        var removed = _permissions.RemoveAll(p => !wanted.Contains(p.PermissionCode));
        var current = _permissions.Select(p => p.PermissionCode).ToHashSet(StringComparer.Ordinal);
        var added = wanted.Except(current, StringComparer.Ordinal).ToList();
        _permissions.AddRange(added.Select(code => new RolePermission(Id, code)));
        return removed > 0 || added.Count > 0;
    }

    /// <summary>Adds permissions without removing any (new permissions of a release joining a system role).</summary>
    public void Grant(IEnumerable<string> permissions) =>
        SetPermissions(_permissions.Select(p => p.PermissionCode).Concat(permissions));

    public bool Has(string permission) => _permissions.Any(p => string.Equals(p.PermissionCode, permission, StringComparison.Ordinal));
}

/// <summary>A permission granted by a role. Audited as its own row: grants and revocations show up in the log.</summary>
[Audited]
public sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(RoleId roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public RoleId RoleId { get; private set; }

    public string PermissionCode { get; private set; } = null!;
}
