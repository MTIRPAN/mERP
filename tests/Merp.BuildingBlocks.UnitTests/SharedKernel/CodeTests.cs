using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class CodeTests
{
    [Theory]
    [InlineData(" holding ", "HOLDING")]
    [InlineData("acme-01", "ACME-01")]
    [InlineData("a_b.c", "A_B.C")]
    // Upper casing ignores the tr-TR culture of the test run: "i" becomes "I", not "İ"
    [InlineData("istanbul", "ISTANBUL")]
    public void Create_NormalizesToInvariantUpperCase(string input, string expected) =>
        Code.Create(input).Value.Value.ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_Empty_IsRequired(string? input) =>
        Code.Create(input).Error.ShouldBe(CommonErrors.Required);

    [Theory]
    [InlineData("İZMİR")]
    [InlineData("A B")]
    [InlineData("ŞUBE1")]
    [InlineData("A/B")]
    public void Create_NonAsciiOrSeparators_IsInvalid(string input) =>
        Code.Create(input).Error.ShouldBe(CommonErrors.InvalidCode);

    [Fact]
    public void Create_TooLong_ReportsMaxLength()
    {
        var error = Code.Create(new string('A', 21)).Error;

        error.Code.ShouldBe("common.errors.max_length");
        error.Args["maxLength"].ShouldBe(20);
        Code.Create("ABCDE", maxLength: 5).IsSuccess.ShouldBeTrue();
    }
}

public class MersisNumberTests
{
    [Theory]
    [InlineData("0123456789000017", "0123456789000017")]
    [InlineData("0123-4567-8900-0017", "0123456789000017")]
    [InlineData(" 0123 4567 8900 0017 ", "0123456789000017")]
    public void Create_SixteenDigits_Succeeds(string input, string expected) =>
        MersisNumber.Create(input).Value.Value.ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("012345678900001")]
    [InlineData("01234567890000170")]
    [InlineData("012345678900001A")]
    public void Create_Invalid_ReturnsError(string? input) =>
        MersisNumber.Create(input).Error.ShouldBe(CommonErrors.InvalidMersisNumber);
}

public class SgkWorkplaceNumberTests
{
    [Fact]
    public void Create_TwentySixDigits_IgnoresGrouping() =>
        SgkWorkplaceNumber.Create("2 1234 01 01 1234567 034 12 34 000").Value.Value.ShouldBe("21234010112345670341234000");

    [Theory]
    [InlineData(null)]
    [InlineData("2123401011234567034123400")]
    [InlineData("212340101123456703412340001")]
    [InlineData("2123401011234567034123400X")]
    public void Create_Invalid_ReturnsError(string? input) =>
        SgkWorkplaceNumber.Create(input).Error.ShouldBe(CommonErrors.InvalidSgkWorkplaceNumber);
}
