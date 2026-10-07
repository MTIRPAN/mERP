using Merp.Modules.Platform.Application.Localization;
using Merp.Modules.Platform.Domain.Localization;

namespace Merp.Modules.Platform.UnitTests.Localization;

public sealed class CultureTests
{
    [Fact]
    public void DefaultCulture_CannotBeSwitchedOffOrDeleted()
    {
        var turkish = Culture.Create("tr-TR", "Türkçe", null, isDefault: true);

        turkish.Update("Türkçe", isActive: false, null).Error.ShouldBe(LocalizationErrors.DefaultCultureRequired);
        turkish.CanDelete().IsFailure.ShouldBeTrue();
        turkish.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Culture_CannotFallBackToItself()
    {
        var english = Culture.Create("en-US", "English", null);

        english.Update("English", isActive: true, "en-US").Error.ShouldBe(LocalizationErrors.SelfFallback);
        english.Update("English", isActive: false, "tr-TR").IsSuccess.ShouldBeTrue();
        (english.IsActive, english.FallbackCode).ShouldBe((false, "tr-TR"));
        english.CanDelete().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void SetText_AddsChangesAndRemoves_ReportingChanges()
    {
        var english = Culture.Create("en-US", "English", null);

        english.SetText("common.errors.not_found", " Record not found. ").ShouldBeTrue();
        english.Texts.ShouldHaveSingleItem().Value.ShouldBe("Record not found.");
        english.SetText("common.errors.not_found", "Record not found.").ShouldBeFalse();
        english.SetText("common.errors.not_found", "Not found.").ShouldBeTrue();
        english.SetText("common.errors.not_found", "  ").ShouldBeTrue();
        english.Texts.ShouldBeEmpty();
        english.SetText("common.errors.not_found", null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("en-us", "en-US")]
    [InlineData(" de-DE ", "de-DE")]
    [InlineData("en", null)]
    [InlineData("xx-YY", null)]
    [InlineData("", null)]
    public void CultureCodes_AcceptOnlySpecificCultures(string code, string? expected) =>
        CultureCodes.Normalize(code).ShouldBe(expected);
}
