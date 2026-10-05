using Merp.Application.Abstractions.Paging;
using Microsoft.EntityFrameworkCore;

namespace Merp.Application.Persistence;

public static class PagingQueryableExtensions
{
    /// <summary>Counts the filtered rows and reads one page. Sort the query first (<see cref="SortMap{T}"/>), then project.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        var total = await query.CountAsync(cancellationToken);
        var items = total == 0
            ? []
            : await query.Skip(request.Skip).Take(request.Size).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, request.PageNumber, request.Size, total);
    }
}
