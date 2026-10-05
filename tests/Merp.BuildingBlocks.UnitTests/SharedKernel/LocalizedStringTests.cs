using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class LocalizedStringTests
{
    [Fact]
    public void Get_FallsBackToDefaultCulture()
    {
        var text = LocalizedString.Of("tr-TR", "Yıllık izin");

        text.Get("tr-TR", "tr-TR").ShouldBe("Yıllık izin");
        text.Get("en-US", "tr-TR").ShouldBe("Yıllık izin");
    }

    [Fact]
    public void Get_ReturnsRequestedCulture_WhenTranslated()
    {
        var text = LocalizedString.Of("tr-TR", "Yıllık izin").With("en-US", "Annual leave");

        text.Get("en-US", "tr-TR").ShouldBe("Annual leave");
        text.Get("EN-us", "tr-TR").ShouldBe("Annual leave");
    }

    [Fact]
    public void Create_IgnoresEmptyTexts_AndNormalizesCultureNames()
    {
        var result = LocalizedString.Create(new Dictionary<string, string?> { ["tr-tr"] = " Evlilik izni ", ["en-US"] = " " });

        result.Value.Values.Count.ShouldBe(1);
        result.Value.Values.Keys.ShouldContain("tr-TR");
        result.Value.Get("tr-TR", "tr-TR").ShouldBe("Evlilik izni");
    }

    [Fact]
    public void Create_WithoutAnyText_ReturnsRequired() =>
        LocalizedString.Create(new Dictionary<string, string?> { ["tr-TR"] = "" }).Error.ShouldBe(CommonErrors.Required);

    [Fact]
    public void Create_WithUnknownCulture_ReturnsInvalidCulture() =>
        LocalizedString.Create(new Dictionary<string, string?> { ["xx-INVALID"] = "a" }).Error.ShouldBe(CommonErrors.InvalidCulture);

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = LocalizedString.Of("tr-TR", "İdari izin").With("en-US", "Administrative leave");
        var b = LocalizedString.Of("en-US", "Administrative leave").With("tr-TR", "İdari izin");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(LocalizedString.Of("tr-TR", "İdari izin"));
    }
}
