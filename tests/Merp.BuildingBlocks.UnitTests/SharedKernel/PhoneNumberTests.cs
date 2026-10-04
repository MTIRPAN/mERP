using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+90 532 123 45 67", "+905321234567")]
    [InlineData("0532 123 45 67", "+905321234567")]
    [InlineData("532-123-45-67", "+905321234567")]
    [InlineData("(0212) 555 12 34", "+902125551234")]
    [InlineData("0850 222 00 00", "+908502220000")]
    [InlineData("0090 532 123 4567", "+905321234567")]
    [InlineData("+49 30 1234567", "+49301234567")]
    [InlineData("+1 (415) 555-2671", "+14155552671")]
    public void Create_ValidNumber_NormalizesToE164(string value, string expected) =>
        PhoneNumber.Create(value).Value.Value.ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")] // too short, no country code
    [InlineData("+90 532 123 45")] // Turkish number too short
    [InlineData("+90 132 123 45 67")] // invalid Turkish prefix
    [InlineData("0532 123 45 67 89")] // Turkish number too long
    [InlineData("+90 532 ABC 45 67")] // letters
    [InlineData("+1234567890123456")] // longer than 15 digits
    public void Create_InvalidNumber_ReturnsError(string? value) =>
        PhoneNumber.Create(value).Error.ShouldBe(CommonErrors.InvalidPhoneNumber);

    [Fact]
    public void IsTurkish_DetectsCountryCode()
    {
        PhoneNumber.Create("05321234567").Value.IsTurkish.ShouldBeTrue();
        PhoneNumber.Create("+49301234567").Value.IsTurkish.ShouldBeFalse();
    }
}
