using Merp.SharedKernel.Numbering;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public sealed class NumberPatternTests
{
    private static readonly DateOnly Date = new(2026, 3, 7);

    [Theory]
    [InlineData("IZN-{yyyy}-{seq:6}", 42, "IZN-2026-000042")]
    [InlineData("{yy}{MM}/{seq}", 7, "2603/7")]
    [InlineData("{seq:3}", 1234, "1234")]
    [InlineData("SCL{seq:5}", 1, "SCL00001")]
    public void Format_FillsTheTokens(string pattern, long value, string expected) =>
        NumberPattern.Format(pattern, Date, value).ShouldBe(expected);

    [Theory]
    [InlineData("IZN-{yyyy}-{seq:6}", NumberResetPeriod.Yearly, null)]
    [InlineData("{yyyy}/{MM}/{seq:4}", NumberResetPeriod.Monthly, null)]
    [InlineData("{seq}", NumberResetPeriod.Never, null)]
    [InlineData("IZN-{seq:6}", NumberResetPeriod.Yearly, NumberPattern.PeriodMissingCode)]
    [InlineData("{yyyy}-{seq}", NumberResetPeriod.Monthly, NumberPattern.PeriodMissingCode)]
    [InlineData("IZN-{yyyy}", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    [InlineData("{seq}-{seq}", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    [InlineData("{seq:0}", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    [InlineData("{seq:13}", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    [InlineData("{dd}-{seq}", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    [InlineData("{yyyy:2}-{seq}", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    [InlineData("A{-{seq}", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    [InlineData("", NumberResetPeriod.Never, NumberPattern.InvalidCode)]
    public void Validate_RequiresOneSequence_AndThePeriodInTheNumber(string pattern, NumberResetPeriod reset, string? error) =>
        NumberPattern.Validate(pattern, reset).ShouldBe(error);

    [Fact]
    public void PeriodKey_SeparatesYearsAndMonths()
    {
        NumberPattern.PeriodKey(NumberResetPeriod.Never, Date).ShouldBe("-");
        NumberPattern.PeriodKey(NumberResetPeriod.Yearly, Date).ShouldBe("2026");
        NumberPattern.PeriodKey(NumberResetPeriod.Monthly, Date).ShouldBe("2026-03");
    }
}
