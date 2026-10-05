using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class TcknTests
{
    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    [InlineData("11111111110")]
    [InlineData("98765432150")]
    [InlineData("34567890170")]
    [InlineData("56789012390")]
    [InlineData(" 10000000146 ")]
    public void Create_ValidNumber_Succeeds(string value)
    {
        var result = Tckn.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value.Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1000000014")] // 10 digits
    [InlineData("100000001460")] // 12 digits
    [InlineData("01000000146")] // starts with 0
    [InlineData("10000000156")] // wrong 10th digit
    [InlineData("10000000147")] // wrong 11th digit
    [InlineData("1000000014a")] // letter
    [InlineData("12345678901")]
    public void Create_InvalidNumber_ReturnsError(string? value)
    {
        var result = Tckn.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CommonErrors.InvalidTckn);
    }

    [Fact]
    public void Masked_HidesMiddleDigits() =>
        Tckn.Create("12345678950").Value.Masked().ShouldBe("123******50");
}
