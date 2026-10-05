using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Postal address. Turkish addresses may carry the province (il, plate number 1–81) and district (ilçe, NVİ code)
/// of the Platform reference data; <see cref="City"/> and <see cref="District"/> then hold their names. Foreign
/// addresses use free text only.
/// </summary>
public sealed record Address
{
    public const string DefaultCountryCode = "TR";
    public const int LineMaxLength = 250;
    public const int NameMaxLength = 100;
    public const int MaxProvinceCode = 81;

    private static readonly Error InvalidValue = Error.Validation("common.errors.invalid_value");

    private Address(string line1, string? line2, string? district, string city, string? postalCode, string countryCode, int? provinceCode, int? districtCode)
    {
        Line1 = line1;
        Line2 = line2;
        District = district;
        City = city;
        PostalCode = postalCode;
        CountryCode = countryCode;
        ProvinceCode = provinceCode;
        DistrictCode = districtCode;
    }

    public string Line1 { get; }

    public string? Line2 { get; }

    /// <summary>District (ilçe).</summary>
    public string? District { get; }

    /// <summary>City or province (il).</summary>
    public string City { get; }

    public string? PostalCode { get; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string CountryCode { get; }

    /// <summary>Province plate number (1–81); Turkish addresses only.</summary>
    public int? ProvinceCode { get; }

    /// <summary>District code of the Platform reference data (NVİ ilçe kodu); Turkish addresses only.</summary>
    public int? DistrictCode { get; }

    public static Result<Address> Create(
        string? line1,
        string? line2,
        string? district,
        string? city,
        string? postalCode,
        string? countryCode = DefaultCountryCode,
        int? provinceCode = null,
        int? districtCode = null)
    {
        var errors = new List<FieldError>();
        line1 = Clean(line1);
        line2 = Clean(line2);
        district = Clean(district);
        city = Clean(city);
        postalCode = Clean(postalCode);
        countryCode = Clean(countryCode)?.ToUpperInvariant() ?? DefaultCountryCode;

        Require(errors, nameof(Line1), line1, LineMaxLength);
        MaxLength(errors, nameof(Line2), line2, LineMaxLength);
        MaxLength(errors, nameof(District), district, NameMaxLength);
        Require(errors, nameof(City), city, NameMaxLength);

        if (countryCode.Length != 2 || !countryCode.All(char.IsAsciiLetterUpper))
        {
            errors.Add(Field(nameof(CountryCode), CommonErrors.InvalidCountryCode));
        }

        var turkish = string.Equals(countryCode, DefaultCountryCode, StringComparison.Ordinal);

        // Turkish postal codes have five digits
        if (postalCode is not null
            && (turkish ? postalCode.Length != 5 || !postalCode.All(char.IsAsciiDigit) : postalCode.Length > 20))
        {
            errors.Add(Field(nameof(PostalCode), CommonErrors.InvalidPostalCode));
        }

        if (provinceCode is not null && (!turkish || provinceCode is < 1 or > MaxProvinceCode))
        {
            errors.Add(Field(nameof(ProvinceCode), InvalidValue));
        }

        // A district only makes sense inside a province
        if (districtCode is not null && (!turkish || provinceCode is null || districtCode <= 0))
        {
            errors.Add(Field(nameof(DistrictCode), InvalidValue));
        }

        return errors.Count > 0
            ? Error.Validation(errors)
            : new Address(line1!, line2, district, city!, postalCode, countryCode, provinceCode, districtCode);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Require(List<FieldError> errors, string field, string? value, int maxLength)
    {
        if (value is null)
        {
            errors.Add(Field(field, CommonErrors.Required));
            return;
        }

        MaxLength(errors, field, value, maxLength);
    }

    private static void MaxLength(List<FieldError> errors, string field, string? value, int maxLength)
    {
        if (value?.Length > maxLength)
        {
            errors.Add(Field(field, CommonErrors.MaxLength(maxLength)));
        }
    }

    private static FieldError Field(string name, Error error) =>
        new(char.ToLowerInvariant(name[0]) + name[1..], error.Code, error.Args);
}
