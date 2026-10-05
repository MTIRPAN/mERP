using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Postal address. Province (il) and district (ilçe) are names; records that need them as references keep the
/// codes next to the address (e.g. Platform's company and branch locations).
/// </summary>
public sealed record Address
{
    public const string DefaultCountryCode = "TR";
    public const int LineMaxLength = 250;
    public const int NameMaxLength = 100;

    // Used by EF Core, which fills the properties through their backing fields.
    private Address()
    {
        Line1 = null!;
        City = null!;
        CountryCode = null!;
    }

    private Address(string line1, string? line2, string? district, string city, string? postalCode, string countryCode)
    {
        Line1 = line1;
        Line2 = line2;
        District = district;
        City = city;
        PostalCode = postalCode;
        CountryCode = countryCode;
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

    public static Result<Address> Create(
        string? line1,
        string? line2,
        string? district,
        string? city,
        string? postalCode,
        string? countryCode = DefaultCountryCode)
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

        // Turkish postal codes have five digits
        if (postalCode is not null
            && (string.Equals(countryCode, DefaultCountryCode, StringComparison.Ordinal)
                ? postalCode.Length != 5 || !postalCode.All(char.IsAsciiDigit)
                : postalCode.Length > 20))
        {
            errors.Add(Field(nameof(PostalCode), CommonErrors.InvalidPostalCode));
        }

        return errors.Count > 0
            ? Error.Validation(errors)
            : new Address(line1!, line2, district, city!, postalCode, countryCode);
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
