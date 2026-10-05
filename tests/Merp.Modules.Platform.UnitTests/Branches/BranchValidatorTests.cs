using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Domain.Branches;

namespace Merp.Modules.Platform.UnitTests.Branches;

public sealed class BranchValidatorTests
{
    private readonly CreateBranchCommandValidator _create = new();

    [Fact]
    public void ValidCommand_Passes() => _create.Validate(Command()).IsValid.ShouldBeTrue();

    [Fact]
    public void Company_IsRequired() =>
        _create.Validate(Command() with { CompanyId = Guid.Empty }).Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("CompanyId");

    [Fact]
    public void UnknownType_Fails() =>
        _create.Validate(Command() with { Type = (BranchType)42 }).Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Type");

    [Fact]
    public void SgkWorkplaceNo_MustHave26Digits() =>
        _create.Validate(Command() with { SgkWorkplaceNo = "1234" }).Errors.ShouldHaveSingleItem().ErrorCode
            .ShouldBe("common.errors.invalid_sgk_workplace_no");

    private static CreateBranchCommand Command() => new(
        Guid.CreateVersion7(), "IST-01", "Kadıköy Mağazası", BranchType.Store, "Moda Cad. No: 1", null, 34, 34023, "34710", null);
}
