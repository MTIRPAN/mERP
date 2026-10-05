using Merp.Modules.Platform.Application.Companies;

namespace Merp.Modules.Platform.UnitTests.Companies;

public sealed class CompanyValidatorTests
{
    private readonly CreateCompanyCommandValidator _create = new();
    private readonly UpdateCompanyCommandValidator _update = new();

    [Fact]
    public void ValidCommand_Passes() => _create.Validate(Command()).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("ACME")]
    [InlineData("acme_01")]
    [InlineData("A-1")]
    public void Code_LettersDigitsDashAndUnderscore_Pass(string code) =>
        _create.Validate(Command() with { Code = code }).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("-ACME", "common.errors.invalid_code")]
    [InlineData("AC ME", "common.errors.invalid_code")]
    [InlineData("ŞİRKET", "common.errors.invalid_code")]
    [InlineData("", "NotEmptyValidator")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", "MaximumLengthValidator")]
    public void Code_Invalid_Fails(string code, string error) =>
        ErrorFor(_create.Validate(Command() with { Code = code }), "Code").ShouldBe(error);

    [Fact]
    public void TaxNumber_WithWrongCheckDigit_Fails() =>
        ErrorFor(_create.Validate(Command() with { TaxNumber = "1234567891" }), "TaxNumber").ShouldBe("common.errors.invalid_vkn");

    [Theory]
    [InlineData("MersisNo", "123", "common.errors.invalid_mersis_no")]
    [InlineData("SgkWorkplaceNo", "1234", "common.errors.invalid_sgk_workplace_no")]
    [InlineData("Phone", "12", "common.errors.invalid_phone_number")]
    [InlineData("Email", "info@", "common.errors.invalid_email")]
    [InlineData("PostalCode", "3471", "common.errors.invalid_postal_code")]
    public void OptionalFields_WhenGiven_MustBeValid(string field, string value, string error)
    {
        var command = field switch
        {
            "MersisNo" => Command() with { MersisNo = value },
            "SgkWorkplaceNo" => Command() with { SgkWorkplaceNo = value },
            "Phone" => Command() with { Phone = value },
            "Email" => Command() with { Email = value },
            _ => Command() with { PostalCode = value },
        };

        ErrorFor(_create.Validate(command), field).ShouldBe(error);
    }

    [Fact]
    public void OptionalFields_WhenBlank_Pass() =>
        _create.Validate(Command() with { MersisNo = " ", SgkWorkplaceNo = "", Phone = null, Email = "", PostalCode = " ", TaxOffice = null })
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("xx-YY", "Europe/Istanbul", "DefaultCulture", "common.errors.invalid_culture")]
    [InlineData("tr-TR", "Europe/Atlantis", "TimeZoneId", "common.errors.invalid_time_zone")]
    public void CultureAndTimeZone_MustExist(string culture, string timeZone, string field, string error) =>
        ErrorFor(_create.Validate(Command() with { DefaultCulture = culture, TimeZoneId = timeZone }), field).ShouldBe(error);

    [Fact]
    public void Location_IsRequired()
    {
        var result = _create.Validate(Command() with { ProvinceCode = 0, DistrictId = 0, AddressLine1 = " " });

        result.Errors.Select(e => e.PropertyName).ShouldBe(["AddressLine1", "ProvinceCode", "DistrictId"], ignoreOrder: true);
    }

    [Fact]
    public void Update_UsesTheSameFieldRules()
    {
        var update = new UpdateCompanyCommand(
            Guid.CreateVersion7(), 1, "ACME A.Ş.", "ACME", null, "1234567891", null, null, "Moda Cad. 1", null, 34, 34023, null, null, null,
            "tr-TR", "Europe/Istanbul", IsActive: true);

        ErrorFor(_update.Validate(update), "TaxNumber").ShouldBe("common.errors.invalid_vkn");
    }

    internal static CreateCompanyCommand Command() => new(
        "ACME",
        "ACME Sanayi ve Ticaret A.Ş.",
        "ACME",
        "Kadıköy",
        "1234567890",
        "0123456789000015",
        null,
        "Caferağa Mah. Moda Cad. No: 1",
        null,
        34,
        34023,
        "34710",
        "0216 123 45 67",
        "info@acme.example.com");

    private static string ErrorFor(FluentValidation.Results.ValidationResult result, string property)
    {
        var error = result.Errors.ShouldHaveSingleItem();
        error.PropertyName.ShouldBe(property);
        return error.ErrorCode;
    }
}
