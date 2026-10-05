using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public sealed class RegistryNumberTests
{
    [Theory]
    [InlineData("0123456789000015", "0123456789000015")]
    [InlineData("0123-4567-8900-0015", "0123456789000015")]
    [InlineData(" 0123 4567 8900 0015 ", "0123456789000015")]
    public void MersisNo_AcceptsSixteenDigits(string input, string expected) =>
        MersisNo.Create(input).Value.Value.ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("012345678900001")]
    [InlineData("01234567890000155")]
    [InlineData("0123456789OOOO15")]
    public void MersisNo_RejectsOtherValues(string? input) =>
        MersisNo.Create(input).Error.ShouldBe(CommonErrors.InvalidMersisNo);

    [Theory]
    [InlineData("21234010112345670341234000")]
    [InlineData("2 1234 01 01 1234567 034 12 34 000")]
    [InlineData("2.1234.01.01.1234567.034.12.34.000")]
    public void SgkWorkplaceNo_AcceptsTwentySixDigits(string input) =>
        SgkWorkplaceNo.Create(input).Value.Value.ShouldBe("21234010112345670341234000");

    [Theory]
    [InlineData("2123401011234567034123400")]
    [InlineData("2123401011234567034123400A")]
    public void SgkWorkplaceNo_RejectsOtherValues(string input) =>
        SgkWorkplaceNo.Create(input).Error.ShouldBe(CommonErrors.InvalidSgkWorkplaceNo);
}
