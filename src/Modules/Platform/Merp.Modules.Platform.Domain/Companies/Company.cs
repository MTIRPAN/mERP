using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Companies;

public readonly record struct CompanyId(Guid Value) : IEntityId<CompanyId>
{
    public static CompanyId From(Guid value) => new(value);
}

/// <summary>A legal entity of the holding (PLAN P1). Other modules refer to it by its id as a plain Guid (D4).</summary>
[Audited]
public sealed class Company : AuditableAggregateRoot<CompanyId>, IHasCustomFields
{
    public const int CodeMaxLength = 20;
    public const int TitleMaxLength = 250;
    public const int ShortNameMaxLength = 100;
    public const int TaxOfficeMaxLength = 100;
    public const string DefaultTimeZoneId = "Europe/Istanbul";

    private Company()
    {
    }

    /// <summary>Short unique code, upper case. It cannot change because integrations refer to it.</summary>
    public string Code { get; private set; } = null!;

    /// <summary>Registered title (ticaret unvanı).</summary>
    public string Title { get; private set; } = null!;

    public string ShortName { get; private set; } = null!;

    public string? TaxOffice { get; private set; }

    public Vkn TaxNumber { get; private set; } = null!;

    public MersisNo? MersisNo { get; private set; }

    public SgkWorkplaceNo? SgkWorkplaceNo { get; private set; }

    public Address Address { get; private set; } = null!;

    public int ProvinceCode { get; private set; }

    public int DistrictId { get; private set; }

    public PhoneNumber? Phone { get; private set; }

    public Email? Email { get; private set; }

    /// <summary>Culture for documents and e-mails of the company, e.g. <c>tr-TR</c>.</summary>
    public string DefaultCulture { get; private set; } = null!;

    /// <summary>IANA time zone; day boundaries of the company are computed in it (D10).</summary>
    public string TimeZoneId { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public CustomFieldValues CustomFields { get; private set; } = CustomFieldValues.Empty;

    public static Company Create(string code, CompanyDetails details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(details);
        var company = new Company
        {
            Id = EntityId.New<CompanyId>(),
            Code = NormalizeCode(code),
            IsActive = true,
        };
        company.Apply(details);
        return company;
    }

    public void Update(CompanyDetails details, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(details);
        Apply(details);
        IsActive = isActive;
    }

    /// <summary>Codes are compared and stored in upper case (culture invariant, CLAUDE.md).</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private void Apply(CompanyDetails details)
    {
        Title = details.Title.Trim();
        ShortName = details.ShortName.Trim();
        TaxOffice = string.IsNullOrWhiteSpace(details.TaxOffice) ? null : details.TaxOffice.Trim();
        TaxNumber = details.TaxNumber;
        MersisNo = details.MersisNo;
        SgkWorkplaceNo = details.SgkWorkplaceNo;
        Address = details.Location.Address;
        ProvinceCode = details.Location.ProvinceCode;
        DistrictId = details.Location.DistrictId;
        Phone = details.Phone;
        Email = details.Email;
        DefaultCulture = details.DefaultCulture;
        TimeZoneId = details.TimeZoneId;
    }
}

/// <summary>Editable data of a company; inputs are validated before they get here.</summary>
public sealed record CompanyDetails(
    string Title,
    string ShortName,
    string? TaxOffice,
    Vkn TaxNumber,
    MersisNo? MersisNo,
    SgkWorkplaceNo? SgkWorkplaceNo,
    Location Location,
    PhoneNumber? Phone,
    Email? Email,
    string DefaultCulture,
    string TimeZoneId);
