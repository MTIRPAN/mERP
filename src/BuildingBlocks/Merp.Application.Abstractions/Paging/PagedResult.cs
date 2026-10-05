namespace Merp.Application.Abstractions.Paging;

/// <summary>One page of a list (CLAUDE.md listing contract).</summary>
/// <param name="Items">Rows of the page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size (at most <see cref="PageRequest.MaxPageSize"/>).</param>
/// <param name="TotalCount">Rows matching the filter on all pages.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>
/// Paging and sorting of a list request. <see cref="Sort"/> is a field name, a leading <c>-</c> sorts descending
/// (<c>-createdAt</c>). Out of range values are clamped instead of rejected.
/// </summary>
public sealed record PageRequest(int? Page = null, int? PageSize = null, string? Sort = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    public int PageNumber => Math.Max(Page ?? 1, 1);

    public int Size => Math.Clamp(PageSize ?? DefaultPageSize, 1, MaxPageSize);

    public int Skip => (PageNumber - 1) * Size;
}
