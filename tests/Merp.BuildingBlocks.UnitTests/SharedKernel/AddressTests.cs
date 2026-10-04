using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class AddressTests
{
    [Fact]
    public void Create_ValidTurkishAddress_TrimsValues()
    {
        var address = Address.Create(" Atatürk Cad. No: 1 ", null, "Kadıköy", " İstanbul ", "34710").Value;

        address.Line1.ShouldBe("Atatürk Cad. No: 1");
        address.City.ShouldBe("İstanbul");
        address.CountryCode.ShouldBe("TR");
    }

    [Fact]
    public void Create_MissingRequiredFields_ReturnsFieldErrors()
    {
        var error = Address.Create(" ", null, null, null, null).Error;

        error.Type.ShouldBe(ErrorType.Validation);
        error.Fields.Select(f => f.Field).ShouldBe(["line1", "city"], ignoreOrder: true);
        error.Fields.ShouldAllBe(f => f.Code == CommonErrors.Required.Code);
    }

    [Theory]
    [InlineData("TR", "3471")]
    [InlineData("TR", "34A10")]
    [InlineData("DE", "123456789012345678901")]
    public void Create_InvalidPostalCode_ReturnsFieldError(string country, string postalCode) =>
        Address.Create("Line", null, null, "City", postalCode, country).Error.Fields.Single().Field.ShouldBe("postalCode");

    [Fact]
    public void Create_InvalidCountryCode_ReturnsFieldError() =>
        Address.Create("Line", null, null, "City", null, "TUR").Error.Fields.Single().Field.ShouldBe("countryCode");

    [Fact]
    public void Create_ForeignAddress_AcceptsNonNumericPostalCode() =>
        Address.Create("10 Downing St", null, null, "London", "SW1A 2AA", "gb").Value.CountryCode.ShouldBe("GB");
}
