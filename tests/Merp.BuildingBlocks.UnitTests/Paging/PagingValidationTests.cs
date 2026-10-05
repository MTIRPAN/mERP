using FluentValidation;
using Merp.Application.Abstractions.Paging;

namespace Merp.BuildingBlocks.UnitTests.Paging;

public sealed class PagingValidationTests
{
    private readonly ListValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("code")]
    public void KnownSortFields_Pass(string? sort) =>
        _validator.Validate(new ListRequest(Sort: sort)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("Name")]
    [InlineData("--name")]
    [InlineData("password")]
    public void UnknownSortFields_Fail(string sort) =>
        _validator.Validate(new ListRequest(Sort: sort)).Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(PagingValidationExtensions.InvalidSortCode);

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, PagingLimits.MaxPageSize + 1)]
    public void PageAndPageSize_OutOfRange_Fail(int page, int pageSize) =>
        _validator.Validate(new ListRequest(page, pageSize)).IsValid.ShouldBeFalse();

    [Fact]
    public void PageSize_UpToTheLimit_Passes() =>
        _validator.Validate(new ListRequest(PageSize: PagingLimits.MaxPageSize)).IsValid.ShouldBeTrue();

    [Fact]
    public void Filter_IsLimited() =>
        _validator.Validate(new ListRequest(Filter: new string('a', PagingLimits.MaxFilterLength + 1))).IsValid.ShouldBeFalse();

    private sealed record ListRequest(int Page = 1, int PageSize = 20, string? Sort = null, string? Filter = null) : IPagedRequest;

    private sealed class ListValidator : AbstractValidator<ListRequest>
    {
        public ListValidator() => this.ValidatePaging(["name", "code"]);
    }
}
