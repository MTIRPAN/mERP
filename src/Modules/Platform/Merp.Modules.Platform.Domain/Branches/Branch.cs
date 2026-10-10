using System.Text.Json.Serialization;
using Merp.Modules.Platform.Domain.Calendars;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Branches;

public readonly record struct BranchId(Guid Value) : IEntityId<BranchId>
{
    public static BranchId From(Guid value) => new(value);
}

/// <summary>Kind of a branch (şube türü). A company has at most one head office.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BranchType>))]
public enum BranchType
{
    HeadOffice,
    Factory,
    Warehouse,
    Store,
    Office,
}

/// <summary>
/// A workplace of a company: head office, factory, warehouse, store or office. The company and the code never
/// change; the code is unique within the company.
/// </summary>
[Audited]
public sealed class Branch : AuditableAggregateRoot<BranchId>, ICompanyScoped
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;

    private Branch()
    {
    }

    public CompanyId CompanyId { get; private set; }

    Guid ICompanyScoped.CompanyId => CompanyId.Value;

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public BranchType Type { get; private set; }

    /// <summary>Address with province and district (il/ilçe) for Turkish branches.</summary>
    public Address Address { get; private set; } = null!;

    public string? SgkWorkplaceNumber { get; private set; }

    public string? Phone { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Work schedule of the branch; null follows the company's default (PLAN P9, D33).</summary>
    public WorkScheduleId? WorkScheduleId { get; private set; }

    public static Branch Create(CompanyId companyId, Code code, BranchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (companyId.Value == Guid.Empty)
        {
            throw new ArgumentException("A branch belongs to a company.", nameof(companyId));
        }

        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A branch code has at most {CodeMaxLength} characters.", nameof(code));
        }

        var branch = new Branch { Id = EntityId.New<BranchId>(), CompanyId = companyId, Code = code.Value, IsActive = true };
        branch.Apply(profile);
        return branch;
    }

    public void Update(BranchProfile profile) => Apply(profile);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private void Apply(BranchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(profile.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile.Type, "Unknown branch type.");
        }

        Name = Text.Required(profile.Name, NameMaxLength, nameof(profile.Name));
        Type = profile.Type;
        Address = profile.Address ?? throw new ArgumentNullException(nameof(profile), "A branch has an address.");
        SgkWorkplaceNumber = profile.SgkWorkplaceNumber?.Value;
        Phone = profile.Phone?.Value;
        WorkScheduleId = profile.WorkScheduleId;
    }
}

/// <summary>Editable details of a branch; the inputs are validated value objects.</summary>
public sealed record BranchProfile(
    string Name,
    BranchType Type,
    Address Address,
    SgkWorkplaceNumber? SgkWorkplaceNumber,
    PhoneNumber? Phone,
    WorkScheduleId? WorkScheduleId = null);
