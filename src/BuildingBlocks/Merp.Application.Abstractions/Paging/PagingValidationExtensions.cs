using FluentValidation;

namespace Merp.Application.Abstractions.Paging;

public static class PagingValidationExtensions
{
    public const string InvalidSortCode = "common.errors.invalid_sort";

    /// <summary>Rules for <c>page</c>, <c>pageSize</c>, <c>sort</c> (one of <paramref name="sortFields"/>) and <c>filter</c>.</summary>
    public static void ValidatePaging<T>(this AbstractValidator<T> validator, IReadOnlyCollection<string> sortFields)
        where T : IPagedRequest
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(sortFields);
        validator.RuleFor(r => r.Page).GreaterThanOrEqualTo(1);
        validator.RuleFor(r => r.PageSize).InclusiveBetween(1, PagingLimits.MaxPageSize);
        validator.RuleFor(r => r.Filter).MaximumLength(PagingLimits.MaxFilterLength);
        validator.RuleFor(r => r.Sort)
            .Must(sort => string.IsNullOrWhiteSpace(sort) || sortFields.Contains(SortParser.Parse(sort).Field, StringComparer.Ordinal))
            .WithErrorCode(InvalidSortCode);
    }
}
