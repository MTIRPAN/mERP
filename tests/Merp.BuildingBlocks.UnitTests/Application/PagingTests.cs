using Merp.Application.Abstractions.Paging;
using Merp.Application.Persistence;

namespace Merp.BuildingBlocks.UnitTests.Application;

public class PagingTests
{
    private sealed record Row(int Id, string Name, int Rank);

    private static readonly SortMap<Row> Sorts = SortMap<Row>.By(r => r.Id)
        .Add("name", r => r.Name)
        .Add("rank", r => r.Rank);

    private static readonly Row[] Rows = [new(3, "c", 1), new(1, "a", 2), new(2, "b", 1), new(4, "a", 1)];

    [Theory]
    [InlineData(null, null, 1, 20, 0)]
    [InlineData(0, 0, 1, 1, 0)]
    [InlineData(3, 50, 3, 50, 100)]
    [InlineData(2, 1000, 2, 200, 200)]
    [InlineData(-5, -1, 1, 1, 0)]
    public void PageRequest_ClampsPageAndSize(int? page, int? size, int expectedPage, int expectedSize, int expectedSkip)
    {
        var request = new PageRequest(page, size);

        request.PageNumber.ShouldBe(expectedPage);
        request.Size.ShouldBe(expectedSize);
        request.Skip.ShouldBe(expectedSkip);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("name", true)]
    [InlineData("-Rank", true)]
    [InlineData("id", false)]
    [InlineData("password", false)]
    public void SortMap_AcceptsOnlyDeclaredFields(string? sort, bool valid) =>
        Sorts.IsValid(sort).ShouldBe(valid);

    [Fact]
    public void SortMap_SortsAscendingWithTieBreaker() =>
        Sorts.Apply(Rows.AsQueryable(), "name", "rank").Select(r => r.Id).ShouldBe([1, 4, 2, 3]);

    [Fact]
    public void SortMap_LeadingDashSortsDescending() =>
        Sorts.Apply(Rows.AsQueryable(), "-rank", "name").Select(r => r.Id).ShouldBe([1, 2, 3, 4]);

    [Fact]
    public void SortMap_EmptySortUsesTheDefault() =>
        Sorts.Apply(Rows.AsQueryable(), " ", "name").First().Id.ShouldBe(1);

    [Theory]
    [InlineData("acme", "%acme%")]
    [InlineData(" 50% ", @"%50\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"c:\x", @"%c:\\x%")]
    public void TextSearch_EscapesLikeWildcards(string term, string pattern) =>
        TextSearch.ContainsPattern(term).ShouldBe(pattern);
}
