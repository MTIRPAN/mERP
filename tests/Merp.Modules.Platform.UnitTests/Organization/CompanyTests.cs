using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.UnitTests.Organization;

public class CompanyTests
{
    internal static CompanyProfile Profile(string title = "Acme Holding A.Ş.", string? culture = null, string? timeZone = null) =>
        new(
            title,
            "Acme",
            " Kadıköy ",
            Vkn.Create("1234567890").Value,
            MersisNumber.Create("0123456789000017").Value,
            null,
            Address.Create("Atatürk Cad. 1", null, "Kadıköy", "İstanbul", "34710", "TR", 34, 1421).Value,
            PhoneNumber.Create("0216 123 45 67").Value,
            Email.Create("Info@Acme.com.tr").Value,
            culture ?? Company.DefaultCulture,
            timeZone ?? Company.DefaultTimeZoneId);

    [Fact]
    public void Create_IsActive_AndStoresNormalizedValues()
    {
        var company = Company.Create(Code.Create("acme").Value, Profile());

        company.Id.Value.ShouldNotBe(Guid.Empty);
        company.Code.ShouldBe("ACME");
        company.IsActive.ShouldBeTrue();
        company.TaxOffice.ShouldBe("Kadıköy");
        company.TaxNumber.ShouldBe("1234567890");
        company.Phone.ShouldBe("+902161234567");
        company.Email.ShouldBe("info@acme.com.tr");
        company.Address!.ProvinceCode.ShouldBe(34);
        company.Culture.ShouldBe("tr-TR");
        company.TimeZoneId.ShouldBe("Europe/Istanbul");
        company.CustomFields.ShouldBe(CustomFieldValues.Empty);
    }

    [Fact]
    public void Update_ChangesDetails_ButNotTheCode()
    {
        var company = Company.Create(Code.Create("ACME").Value, Profile());

        company.Update(Profile("Acme Sanayi A.Ş.", culture: "en-us", timeZone: "Europe/Berlin"));

        company.Code.ShouldBe("ACME");
        company.Title.ShouldBe("Acme Sanayi A.Ş.");
        company.Culture.ShouldBe("en-US");
        company.TimeZoneId.ShouldBe("Europe/Berlin");
    }

    [Fact]
    public void Deactivate_AndActivate_ToggleTheFlag()
    {
        var company = Company.Create(Code.Create("ACME").Value, Profile());

        company.Deactivate();
        company.IsActive.ShouldBeFalse();
        company.Activate();
        company.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("  ")]
    [InlineData(null)]
    public void Create_WithoutTitle_Throws(string? title) =>
        Should.Throw<ArgumentException>(() => Company.Create(Code.Create("ACME").Value, Profile(title!)));

    [Fact]
    public void Create_WithUnknownTimeZoneOrCulture_Throws()
    {
        Should.Throw<ArgumentException>(() => Company.Create(Code.Create("ACME").Value, Profile(timeZone: "Mars/Olympus")));
        Should.Throw<ArgumentException>(() => Company.Create(Code.Create("ACME").Value, Profile(culture: "tr")));
    }

    [Fact]
    public void Create_WithTooLongCode_Throws() =>
        Should.Throw<ArgumentException>(() => Company.Create(Code.Create(new string('A', 25), maxLength: 30).Value, Profile()));

    [Theory]
    [InlineData("Europe/Istanbul", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    public void IsKnownTimeZone(string timeZone, bool known) => Company.IsKnownTimeZone(timeZone).ShouldBe(known);

    [Theory]
    [InlineData("tr-TR", true)]
    [InlineData("en-US", true)]
    [InlineData("tr", false)]
    [InlineData("xx-XX", false)]
    public void IsKnownCulture(string culture, bool known) => Company.IsKnownCulture(culture).ShouldBe(known);
}
