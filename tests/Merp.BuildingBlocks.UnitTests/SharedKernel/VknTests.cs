using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class VknTests
{
    [Theory]
    [InlineData("1234567890")]
    [InlineData("0000000001")]
    [InlineData("1111111114")]
    [InlineData("9876543217")]
    [InlineData("4700036210")]
    [InlineData("0123456789")]
    public void Create_ValidNumber_Succeeds(string value) =>
        Vkn.Create(value).Value.Value.ShouldBe(value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")] // 9 digits
    [InlineData("12345678901")] // 11 digits
    [InlineData("1234567891")] // wrong check digit
    [InlineData("0000000000")] // wrong check digit
    [InlineData("12345678a0")] // letter
    public void Create_InvalidNumber_ReturnsError(string? value) =>
        Vkn.Create(value).Error.ShouldBe(CommonErrors.InvalidVkn);
}
