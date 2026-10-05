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

public enum BranchType
{
    Headquarters,
    Factory,
    Warehouse,
    Store,
    Office,
}

/// <summary>A workplace of a company: head office, factory, warehouse, store or office (PLAN P1).</summary>
[Audited]
public sealed class Branch : AuditableAggregateRoot<BranchId>, ICompanyScoped
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;

    private Branch()
    {
    }

    public CompanyId CompanyId { get; private set; }

    /// <summary>Other modules see the company as a plain Guid (D4).</summary>
    Guid ICompanyScoped.CompanyId => CompanyId.Value;

    /// <summary>Unique within the company, upper case; it cannot change.</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public BranchType Type { get; private set; }

    public Address Address { get; private set; } = null!;

    public int ProvinceCode { get; private set; }

    public int DistrictId { get; private set; }

    public SgkWorkplaceNo? SgkWorkplaceNo { get; private set; }

    public bool IsActive { get; private set; }

    public static Branch Create(CompanyId company, string code, BranchDetails details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(details);
        var branch = new Branch
        {
            Id = EntityId.New<BranchId>(),
            CompanyId = company,
            Code = Companies.Company.NormalizeCode(code),
            IsActive = true,
        };
        branch.Apply(details);
        return branch;
    }

    public void Update(BranchDetails details, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(details);
        Apply(details);
        IsActive = isActive;
    }

    private void Apply(BranchDetails details)
    {
        Name = details.Name.Trim();
        Type = details.Type;
        Address = details.Location.Address;
        ProvinceCode = details.Location.ProvinceCode;
        DistrictId = details.Location.DistrictId;
        SgkWorkplaceNo = details.SgkWorkplaceNo;
    }
}

public sealed record BranchDetails(string Name, BranchType Type, Location Location, SgkWorkplaceNo? SgkWorkplaceNo);
