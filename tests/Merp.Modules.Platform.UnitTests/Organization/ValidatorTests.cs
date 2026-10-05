using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Branches;

namespace Merp.Modules.Platform.UnitTests.Organization;

public class ValidatorTests
{
    private static CreateCompanyCommand Company(
        string? code = "ACME",
        string? taxNumber = "1234567890",
        string? mersis = null,
        string? phone = null,
        string? email = null,
        string? culture = null,
        string? timeZone = null) =>
        new(code, "Acme Holding A.Ş.", "Acme", null, taxNumber, mersis, null, null, phone, email, culture, timeZone);

    [Fact]
    public void CreateCompany_ValidInput_Passes() =>
        new CreateCompanyCommandValidator().Validate(Company(mersis: "0123456789000017", phone: "0216 123 45 67", email: "info@acme.com")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("1234567891", "common.errors.invalid_vkn")]
    [InlineData("12345", "common.errors.invalid_vkn")]
    [InlineData("", "NotEmptyValidator")]
    public void CreateCompany_InvalidTaxNumber_IsAFieldError(string taxNumber, string code)
    {
        var failure = new CreateCompanyCommandValidator().Validate(Company(taxNumber: taxNumber)).Errors.ShouldHaveSingleItem();

        failure.PropertyName.ShouldBe(nameof(CreateCompanyCommand.TaxNumber));
        failure.ErrorCode.ShouldBe(code);
    }

    [Theory]
    [InlineData("İZMİR", "common.errors.invalid_code")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", "common.errors.max_length")]
    public void CreateCompany_InvalidCode_IsAFieldError(string code, string error) =>
        new CreateCompanyCommandValidator().Validate(Company(code: code)).Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(error);

    [Fact]
    public void CreateCompany_InvalidOptionalFields_AreReported()
    {
        var result = new CreateCompanyCommandValidator().Validate(
            Company(mersis: "123", phone: "12", email: "x@", culture: "klingon", timeZone: "Mars/Olympus"));

        result.Errors.Select(e => (e.PropertyName, e.ErrorCode)).ShouldBe(
        [
            ("MersisNumber", "common.errors.invalid_mersis_number"),
            ("Phone", "common.errors.invalid_phone_number"),
            ("Email", "common.errors.invalid_email"),
            ("Culture", "common.errors.invalid_culture"),
            ("TimeZoneId", "common.errors.invalid_time_zone"),
        ],
        ignoreOrder: true);
    }

    [Fact]
    public void UpdateCompany_UsesTheSameFieldRules() =>
        new UpdateCompanyCommandValidator()
            .Validate(new UpdateCompanyCommand(1, "", "Acme", null, "1234567891", null, null, null, null, null, null, null, true))
            .Errors.Select(e => e.PropertyName).ShouldBe(["Title", "TaxNumber"], ignoreOrder: true);

    [Fact]
    public void CreateBranch_RequiresCompanyCodeNameTypeAndAddress()
    {
        var result = new CreateBranchCommandValidator().Validate(new CreateBranchCommand(Guid.Empty, null, null, null, null, null, null));

        result.Errors.Select(e => e.PropertyName).Distinct().ShouldBe(["CompanyId", "Code", "Name", "Type", "Address"], ignoreOrder: true);
    }

    [Fact]
    public void CreateBranch_ValidInput_Passes() =>
        new CreateBranchCommandValidator()
            .Validate(new CreateBranchCommand(Guid.CreateVersion7(), "GBZ", "Gebze", BranchType.Factory, new AddressInput("OSB", ProvinceCode: 41, DistrictCode: 1338), null, "0262 123 45 67"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("name")]
    [InlineData("-code")]
    [InlineData(null)]
    public void ListBranches_AcceptsDeclaredSorts(string? sort) =>
        new GetBranchesQueryValidator().Validate(new GetBranchesQuery(new(Sort: sort))).IsValid.ShouldBeTrue();

    [Fact]
    public void ListCompanies_RejectsUnknownSort() =>
        new GetCompaniesQueryValidator().Validate(new GetCompaniesQuery(new(Sort: "secret")))
            .Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("common.errors.invalid_sort");
}
