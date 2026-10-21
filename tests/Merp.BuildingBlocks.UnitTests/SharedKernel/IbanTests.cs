using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class IbanTests
{
    [Theory]
    [InlineData("TR330006100519786457841326")]
    [InlineData("TR850001000000000012345678")]
    [InlineData("TR180006200001000006295356")]
    [InlineData("TR190012300000000000000001")]
    [InlineData("TR810004600688800000618047")]
    [InlineData("TR510006400000123456789012")]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB82WEST12345698765432")]
    public void Create_ValidIban_Succeeds(string value) =>
        Iban.Create(value).Value.Value.ShouldBe(value);

    [Fact]
    public void Create_NormalizesSpacesAndCase()
    {
        var iban = Iban.Create("tr33 0006 1005 1978 6457 8413 26").Value;

        iban.Value.ShouldBe("TR330006100519786457841326");
        iban.CountryCode.ShouldBe("TR");
        iban.Formatted().ShouldBe("TR33 0006 1005 1978 6457 8413 26");
    }

    [Fact]
    public void Masked_ShowsTheCountryAndTheLastFourCharacters() =>
        Iban.Create("TR330006100519786457841326").Value.Masked().ShouldBe("TR** **** … 1326");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TR340006100519786457841326")] // wrong check digits
    [InlineData("TR33000610051978645784132")] // 25 characters
    [InlineData("TR3300061005197864578413261")] // 27 characters
    [InlineData("TR33000610051978645784132A")] // letter in Turkish BBAN
    [InlineData("DE89370400440532013001")] // wrong account number
    [InlineData("1234")] // too short
    [InlineData("TR33-0006-1005-1978-6457-8413-26")] // invalid characters
    public void Create_InvalidIban_ReturnsError(string? value) =>
        Iban.Create(value).Error.ShouldBe(CommonErrors.InvalidIban);
}
