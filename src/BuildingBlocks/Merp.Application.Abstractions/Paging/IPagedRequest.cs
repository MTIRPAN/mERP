namespace Merp.Application.Abstractions.Paging;

/// <summary>
/// List request (CLAUDE.md list contract): <c>page</c> (from 1), <c>pageSize</c> (at most
/// <see cref="PagingLimits.MaxPageSize"/>), <c>sort</c> (field name, a leading <c>-</c> sorts descending) and
/// <c>filter</c> (free text search; lists add their own typed filters).
/// </summary>
public interface IPagedRequest
{
    int Page { get; }

    int PageSize { get; }

    string? Sort { get; }

    string? Filter { get; }
}

public static class PagingLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;
    public const int MaxFilterLength = 200;
}
