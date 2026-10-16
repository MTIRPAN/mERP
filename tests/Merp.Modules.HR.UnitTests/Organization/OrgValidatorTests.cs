using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;

namespace Merp.Modules.HR.UnitTests.Organization;

public class OrgValidatorTests
{
    [Fact]
    public void CreateOrgUnit_WithValidInput_Passes() =>
        new CreateOrgUnitCommandValidator()
            .Validate(new CreateOrgUnitCommand(Guid.CreateVersion7(), null, "satis", "Satış", OrgUnitType.Directorate))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void CreateOrgUnit_ReportsEachInvalidField()
    {
        var result = new CreateOrgUnitCommandValidator().Validate(new CreateOrgUnitCommand(
            Guid.Empty,
            null,
            "SATIŞ 1",
            "",
            (OrgUnitType)42,
            new string('x', OrgUnit.CostCenterCodeMaxLength + 1),
            null,
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 31)));

        result.Errors.Select(e => e.PropertyName).ShouldBe(["CompanyId", "Code", "Name", "Type", "CostCenterCode", "ValidTo"], ignoreOrder: true);
        result.Errors.Single(e => e.PropertyName == "Code").ErrorCode.ShouldBe("common.errors.invalid_code");
        result.Errors.Single(e => e.PropertyName == "ValidTo").ErrorCode.ShouldBe("common.errors.invalid_date_range");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(Position.MaxNorm, true)]
    [InlineData(Position.MaxNorm + 1, false)]
    public void CreatePosition_AcceptsOneTo999Seats(int norm, bool valid) =>
        new CreatePositionCommandValidator()
            .Validate(new CreatePositionCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "P-1", Norm: norm))
            .IsValid.ShouldBe(valid);

    [Fact]
    public void UpdatePosition_NeedsAStatus() =>
        new UpdatePositionCommandValidator()
            .Validate(new UpdatePositionCommand(1, Guid.CreateVersion7(), Guid.CreateVersion7(), null, 1, null, false))
            .Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Status");

    [Fact]
    public void CreateJob_NeedsACodeAndAName()
    {
        var result = new CreateJobCommandValidator().Validate(new CreateJobCommand(null, " ", Level: new string('M', Job.LevelMaxLength + 1)));

        result.Errors.Select(e => e.PropertyName).ShouldBe(["Code", "Name", "Level"], ignoreOrder: true);
    }
}
