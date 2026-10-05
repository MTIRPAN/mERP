using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class DateRangeTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jan31 = new(2026, 1, 31);

    [Fact]
    public void Create_EndBeforeStart_ReturnsError() =>
        DateRange.Create(Jan31, Jan1).Error.ShouldBe(CommonErrors.InvalidDateRange);

    [Fact]
    public void Create_SingleDay_IsValid() =>
        DateRange.Create(Jan1, Jan1).Value.TotalDays.ShouldBe(1);

    [Fact]
    public void Contains_IsInclusive()
    {
        var range = DateRange.Create(Jan1, Jan31).Value;

        range.Contains(Jan1).ShouldBeTrue();
        range.Contains(Jan31).ShouldBeTrue();
        range.Contains(Jan31.AddDays(1)).ShouldBeFalse();
        range.Contains(Jan1.AddDays(-1)).ShouldBeFalse();
    }

    [Fact]
    public void OpenEndedRange_ContainsAllLaterDates()
    {
        var range = DateRange.Create(Jan1).Value;

        range.IsOpenEnded.ShouldBeTrue();
        range.TotalDays.ShouldBeNull();
        range.Contains(new DateOnly(2099, 12, 31)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("2026-01-15", "2026-02-15", true)]
    [InlineData("2026-01-31", "2026-02-15", true)] // touching end is an overlap (inclusive)
    [InlineData("2026-02-01", "2026-02-15", false)]
    [InlineData("2025-12-01", "2025-12-31", false)]
    [InlineData("2025-12-01", null, true)]
    public void Overlaps(string start, string? end, bool expected)
    {
        var range = DateRange.Create(Jan1, Jan31).Value;
        var other = DateRange.Create(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), end is null ? null : DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)).Value;

        range.Overlaps(other).ShouldBe(expected);
        other.Overlaps(range).ShouldBe(expected);
    }
}
