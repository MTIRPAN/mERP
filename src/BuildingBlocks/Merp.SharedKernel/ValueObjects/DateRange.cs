using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>Inclusive range of calendar dates. A missing <see cref="End"/> means open ended.</summary>
public sealed record DateRange
{
    private DateRange(DateOnly start, DateOnly? end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly? End { get; }

    public bool IsOpenEnded => End is null;

    public static Result<DateRange> Create(DateOnly start, DateOnly? end = null) =>
        end < start ? CommonErrors.InvalidDateRange : new DateRange(start, end);

    public bool Contains(DateOnly date) => date >= Start && (End is null || date <= End);

    public bool Overlaps(DateRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return (End is null || other.Start <= End) && (other.End is null || Start <= other.End);
    }

    /// <summary>Number of calendar days including both ends; null for an open-ended range.</summary>
    public int? TotalDays => End is { } end ? end.DayNumber - Start.DayNumber + 1 : null;
}
