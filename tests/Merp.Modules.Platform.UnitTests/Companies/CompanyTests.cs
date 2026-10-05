using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.UnitTests.Companies;

public sealed class CompanyTests
{
    [Fact]
    public void Create_NormalizesCodeAndTrimsTexts()
    {
        var company = Company.Create(" acme-01 ", Details(title: "  ACME Sanayi ve Ticaret A.Ş. ", shortName: " ACME "));

        company.Id.Value.ShouldNotBe(Guid.Empty);
        company.Code.ShouldBe("ACME-01");
        company.Title.ShouldBe("ACME Sanayi ve Ticaret A.Ş.");
        company.ShortName.ShouldBe("ACME");
        company.IsActive.ShouldBeTrue();
        company.TimeZoneId.ShouldBe(Company.DefaultTimeZoneId);
    }

    [Fact]
    public void NormalizeCode_IsCultureInvariant()
    {
        // In tr-TR "i".ToUpper() is "İ"; codes must not depend on the server culture (CLAUDE.md).
        Company.NormalizeCode("istanbul").ShouldBe("ISTANBUL");
    }

    [Fact]
    public void Create_KeepsLocationCodesAndAddress()
    {
        var company = Company.Create("ACME", Details());

        company.ProvinceCode.ShouldBe(34);
        company.DistrictId.ShouldBe(34023);
        company.Address.City.ShouldBe("İstanbul");
        company.Address.District.ShouldBe("Kadıköy");
    }

    [Fact]
    public void Create_BlankTaxOffice_IsStoredAsNull() =>
        Company.Create("ACME", Details(taxOffice: "  ")).TaxOffice.ShouldBeNull();

    [Fact]
    public void Update_ReplacesDetailsAndActiveState_ButNotTheCode()
    {
        var company = Company.Create("ACME", Details());

        company.Update(Details(title: "ACME Holding A.Ş.", shortName: "ACME Holding"), isActive: false);

        company.Code.ShouldBe("ACME");
        company.Title.ShouldBe("ACME Holding A.Ş.");
        company.ShortName.ShouldBe("ACME Holding");
        company.IsActive.ShouldBeFalse();
    }

    internal static Location IstanbulKadikoy() =>
        new(Address.Create("Caferağa Mah. Moda Cad. No: 1", null, "Kadıköy", "İstanbul", "34710").Value, 34, 34023);

    private static CompanyDetails Details(string title = "ACME A.Ş.", string shortName = "ACME", string? taxOffice = "Kadıköy") => new(
        title,
        shortName,
        taxOffice,
        Vkn.Create("1234567890").Value,
        null,
        null,
        IstanbulKadikoy(),
        PhoneNumber.Create("0216 123 45 67").Value,
        Email.Create("info@acme.example.com").Value,
        "tr-TR",
        Company.DefaultTimeZoneId);
}
