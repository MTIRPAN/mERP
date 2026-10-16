using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Organization;

public readonly record struct OrgUnitId(Guid Value) : IEntityId<OrgUnitId>
{
    public static OrgUnitId From(Guid value) => new(value);
}

/// <summary>Kind of an organization unit (birim türü).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrgUnitType>))]
public enum OrgUnitType
{
    Directorate,
    Department,
    Unit,
    Team,
}

/// <summary>
/// A unit of a company's organization: directorate, department, unit or team (D37). The company and the code never
/// change; the code is unique within the company. <see cref="Path"/> is the PostgreSQL <c>ltree</c> of the ids from
/// the root unit down to this one, so a subtree is one indexed query.
/// </summary>
[Audited]
public sealed class OrgUnit : AuditableAggregateRoot<OrgUnitId>, ICompanyScoped
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;
    public const int CostCenterCodeMaxLength = 30;

    private OrgUnit()
    {
    }

    /// <summary>The company (Platform's id).</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Parent unit in the same company; null for a top unit.</summary>
    public OrgUnitId? ParentId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public OrgUnitType Type { get; private set; }

    /// <summary>The position that manages the unit; a position of this unit.</summary>
    public PositionId? ManagerPositionId { get; private set; }

    public string? CostCenterCode { get; private set; }

    /// <summary>The branch (workplace) of the unit (Platform's id).</summary>
    public Guid? BranchId { get; private set; }

    /// <summary>First day of the unit; null when it has always existed.</summary>
    public DateOnly? ValidFrom { get; private set; }

    /// <summary>Last day of the unit; null while it exists.</summary>
    public DateOnly? ValidTo { get; private set; }

    /// <summary>Ids (32 hex digits) from the top unit down to this one, separated by dots.</summary>
    public string Path { get; private set; } = null!;

    public static OrgUnit Create(Guid companyId, OrgUnit? parent, Code code, OrgUnitProfile profile)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A unit belongs to a company.", nameof(companyId));
        }

        if (parent is not null && parent.CompanyId != companyId)
        {
            throw new ArgumentException("The parent unit belongs to another company.", nameof(parent));
        }

        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A unit code has at most {CodeMaxLength} characters.", nameof(code));
        }

        var unit = new OrgUnit { Id = EntityId.New<OrgUnitId>(), CompanyId = companyId, Code = code.Value };
        unit.ParentId = parent?.Id;
        unit.Path = OrgPaths.Child(parent?.Path, unit.Id);
        unit.Apply(profile);
        return unit;
    }

    public void Update(OrgUnitProfile profile) => Apply(profile);

    /// <summary>Sets the manager position; the reporting lines of the company follow (D37).</summary>
    public void ChangeManager(PositionId? managerPositionId)
    {
        if (ManagerPositionId == managerPositionId)
        {
            return;
        }

        ManagerPositionId = managerPositionId;
        Raise(new ReportingStructureChanged(CompanyId));
    }

    /// <summary>
    /// Moves the unit under <paramref name="parent"/> of the same company, or to the top with null. The sub units
    /// follow: <see cref="OrgUnitMoved"/> updates their paths in the same transaction.
    /// </summary>
    public Result MoveTo(OrgUnit? parent)
    {
        if (parent is not null)
        {
            if (parent.CompanyId != CompanyId)
            {
                return OrgUnitErrors.ParentInOtherCompany;
            }

            if (OrgPaths.IsWithin(parent.Path, Path))
            {
                return OrgUnitErrors.MoveIntoOwnSubtree;
            }
        }

        if (ParentId == parent?.Id)
        {
            return Result.Success();
        }

        var oldPath = Path;
        ParentId = parent?.Id;
        Path = OrgPaths.Child(parent?.Path, Id);
        Raise(new OrgUnitMoved(Id, CompanyId, oldPath, Path));
        Raise(new ReportingStructureChanged(CompanyId));
        return Result.Success();
    }

    /// <summary>The unit exists on <paramref name="day"/> (both ends inclusive).</summary>
    public bool IsActiveOn(DateOnly day) => (ValidFrom is null || ValidFrom <= day) && (ValidTo is null || ValidTo >= day);

    private void Apply(OrgUnitProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(profile.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile.Type, "Unknown unit type.");
        }

        if (profile.ValidFrom is not null && profile.ValidTo is not null && profile.ValidTo < profile.ValidFrom)
        {
            throw new ArgumentException("A unit cannot end before it starts.", nameof(profile));
        }

        Name = Text.Required(profile.Name, NameMaxLength, nameof(profile.Name));
        Type = profile.Type;
        CostCenterCode = Text.Optional(profile.CostCenterCode, CostCenterCodeMaxLength, nameof(profile.CostCenterCode));
        BranchId = profile.BranchId;
        ValidFrom = profile.ValidFrom;
        ValidTo = profile.ValidTo;
    }
}

/// <summary>Editable details of a unit.</summary>
public sealed record OrgUnitProfile(
    string Name,
    OrgUnitType Type,
    string? CostCenterCode,
    Guid? BranchId,
    DateOnly? ValidFrom,
    DateOnly? ValidTo);

/// <summary>
/// A unit moved: every sub unit's path starting with <paramref name="OldPath"/> now starts with
/// <paramref name="NewPath"/>.
/// </summary>
public sealed record OrgUnitMoved(OrgUnitId UnitId, Guid CompanyId, string OldPath, string NewPath) : IDomainEvent;

public static class OrgUnitErrors
{
    public static readonly Error ParentInOtherCompany = Error.Validation([new FieldError("parentId", "hr.org_units.errors.parent_in_other_company")]);
    public static readonly Error MoveIntoOwnSubtree = Error.Validation([new FieldError("parentId", "hr.org_units.errors.move_into_own_subtree")]);
}

/// <summary>Unit paths: <c>ltree</c> labels are the unit ids as 32 hex digits.</summary>
public static class OrgPaths
{
    public static string Label(OrgUnitId id) => id.Value.ToString("N");

    public static string Child(string? parentPath, OrgUnitId id) =>
        parentPath is null ? Label(id) : $"{parentPath}.{Label(id)}";

    /// <summary><paramref name="path"/> is <paramref name="ancestorPath"/> or below it (ltree <c>&lt;@</c>).</summary>
    public static bool IsWithin(string path, string ancestorPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(ancestorPath);
        return string.Equals(path, ancestorPath, StringComparison.Ordinal)
            || path.StartsWith(ancestorPath + ".", StringComparison.Ordinal);
    }
}
