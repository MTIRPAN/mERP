using System.Globalization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Companies;

public readonly record struct CompanyId(Guid Value) : IEntityId<CompanyId>
{
    public static CompanyId From(Guid value) => new(value);
}

/// <summary>
/// A legal entity of the tenant (a holding company or one of its subsidiaries). Records of other modules refer to
/// it with a plain <c>CompanyId</c> (D4). The code never changes because other modules and integrations keep it.
/// </summary>
[Audited]
public sealed class Company : AuditableAggregateRoot<CompanyId>, IHasCustomFields
{
    public const int CodeMaxLength = 20;
    public const int TitleMaxLength = 250;
    public const int ShortNameMaxLength = 100;
    public const int TaxOfficeMaxLength = 100;
    public const string DefaultCulture = "tr-TR";
    public const string DefaultTimeZoneId = "Europe/Istanbul";

    private Company()
    {
    }

    public string Code { get; private set; } = null!;

    /// <summary>Full legal name (ticaret unvanı).</summary>
    public string Title { get; private set; } = null!;

    /// <summary>Name shown in lists and the company selector.</summary>
    public string ShortName { get; private set; } = null!;

    public string? TaxOffice { get; private set; }

    /// <summary>Tax identification number (VKN), unique in the tenant.</summary>
    public string TaxNumber { get; private set; } = null!;

    public string? MersisNumber { get; private set; }

    public string? SgkWorkplaceNumber { get; private set; }

    public Address? Address { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    /// <summary>Culture of documents and messages produced for the company, e.g. <c>tr-TR</c>.</summary>
    public string Culture { get; private set; } = DefaultCulture;

    /// <summary>IANA time zone used for the company's day boundaries (D10).</summary>
    public string TimeZoneId { get; private set; } = DefaultTimeZoneId;

    public bool IsActive { get; private set; }

    public CustomFieldValues CustomFields { get; private set; } = CustomFieldValues.Empty;

    public static Company Create(Code code, CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A company code has at most {CodeMaxLength} characters.", nameof(code));
        }

        var company = new Company { Id = EntityId.New<CompanyId>(), Code = code.Value, IsActive = true };
        company.Apply(profile);
        return company;
    }

    public void Update(CompanyProfile profile) => Apply(profile);

    /// <summary>Values checked by the application against the custom field definitions (D12).</summary>
    public void SetCustomFields(CustomFieldValues values) => CustomFields = values ?? throw new ArgumentNullException(nameof(values));

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>True for a time zone id the runtime knows (IANA ids such as <c>Europe/Istanbul</c>).</summary>
    public static bool IsKnownTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);

    /// <summary>True for a specific culture name such as <c>tr-TR</c>.</summary>
    public static bool IsKnownCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return false;
        }

        try
        {
            return !CultureInfo.GetCultureInfo(culture, predefinedOnly: true).IsNeutralCulture;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private void Apply(CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Title = Text.Required(profile.Title, TitleMaxLength, nameof(profile.Title));
        ShortName = Text.Required(profile.ShortName, ShortNameMaxLength, nameof(profile.ShortName));
        TaxOffice = Text.Optional(profile.TaxOffice, TaxOfficeMaxLength, nameof(profile.TaxOffice));
        TaxNumber = (profile.TaxNumber ?? throw new ArgumentNullException(nameof(profile), "The tax number is required.")).Value;
        MersisNumber = profile.MersisNumber?.Value;
        SgkWorkplaceNumber = profile.SgkWorkplaceNumber?.Value;
        Address = profile.Address;
        Phone = profile.Phone?.Value;
        Email = profile.Email?.Value;

        Culture = IsKnownCulture(profile.Culture)
            ? CultureInfo.GetCultureInfo(profile.Culture).Name
            : throw new ArgumentException($"Unknown culture '{profile.Culture}'.", nameof(profile));
        TimeZoneId = IsKnownTimeZone(profile.TimeZoneId)
            ? profile.TimeZoneId
            : throw new ArgumentException($"Unknown time zone '{profile.TimeZoneId}'.", nameof(profile));
    }
}

/// <summary>Editable details of a company; the inputs are validated value objects.</summary>
public sealed record CompanyProfile(
    string Title,
    string ShortName,
    string? TaxOffice,
    Vkn TaxNumber,
    MersisNumber? MersisNumber,
    SgkWorkplaceNumber? SgkWorkplaceNumber,
    Address? Address,
    PhoneNumber? Phone,
    Email? Email,
    string Culture = Company.DefaultCulture,
    string TimeZoneId = Company.DefaultTimeZoneId);
