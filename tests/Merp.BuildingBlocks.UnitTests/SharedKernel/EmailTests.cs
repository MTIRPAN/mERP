using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class EmailTests
{
    [Theory]
    [InlineData("ali.veli@example.com", "ali.veli@example.com")]
    [InlineData("  Ayse.Yilmaz@Firma.COM.TR ", "ayse.yilmaz@firma.com.tr")]
    [InlineData("ik+aday@merp.local.dev", "ik+aday@merp.local.dev")]
    [InlineData("İLKER@ÖRNEK.com", null)] // non-ASCII domain is rejected
    public void Create_NormalizesOrRejects(string value, string? expected)
    {
        var result = Email.Create(value);

        if (expected is null)
        {
            result.IsFailure.ShouldBeTrue();
        }
        else
        {
            result.Value.Value.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ali")]
    [InlineData("ali@")]
    [InlineData("@example.com")]
    [InlineData("ali@example")]
    [InlineData("ali veli@example.com")]
    [InlineData("ali@@example.com")]
    [InlineData("ali@-example.com")]
    public void Create_InvalidAddress_ReturnsError(string? value) =>
        Email.Create(value).Error.ShouldBe(CommonErrors.InvalidEmail);

    [Fact]
    public void Create_UsesInvariantLowerCase_ForTurkishCapitalI() =>
        // Under tr-TR "I".ToLower() would produce dotless "ı"; invariant lower case must keep "i".
        Email.Create("ILKER@EXAMPLE.COM").Value.Value.ShouldBe("ilker@example.com");
}
