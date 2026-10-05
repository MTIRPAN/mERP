namespace Merp.Application.Abstractions.Paging;

/// <summary>One page of a list (CLAUDE.md list contract).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
