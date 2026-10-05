using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Merp.Application.Abstractions.Paging;

/// <summary>Fields a list can be sorted by: request name (camelCase) → key selector on the entity.</summary>
public sealed class SortMap<TEntity>
{
    private readonly Dictionary<string, LambdaExpression> _fields = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Names => _fields.Keys;

    public SortMap<TEntity> Add<TKey>(string name, Expression<Func<TEntity, TKey>> key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(key);
        _fields.Add(name, key);
        return this;
    }

    internal LambdaExpression this[string name] =>
        _fields.TryGetValue(name, out var key) ? key : throw new ArgumentException($"Unknown sort field '{name}'.", nameof(name));
}

public static class PagingQueryableExtensions
{
    /// <summary>
    /// Sorts the entity query by the request's <c>sort</c> (or <paramref name="defaultSort"/>), then by
    /// <paramref name="tieBreaker"/> so pages are stable, projects one page and returns it with the total count.
    /// Validate <c>sort</c> with <see cref="PagingValidationExtensions.ValidatePaging{T}"/> using
    /// <see cref="SortMap{TEntity}.Names"/> first.
    /// </summary>
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TEntity, TDto>(
        this IQueryable<TEntity> query,
        IPagedRequest request,
        SortMap<TEntity> sorts,
        string defaultSort,
        string tieBreaker,
        Expression<Func<TEntity, TDto>> projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sorts);

        var total = await query.CountAsync(cancellationToken);
        var (field, descending) = SortParser.Parse(string.IsNullOrWhiteSpace(request.Sort) ? defaultSort : request.Sort);
        var ordered = Order(query, sorts[field], descending, thenBy: false);
        if (!string.Equals(field, tieBreaker, StringComparison.Ordinal))
        {
            ordered = Order(ordered, sorts[tieBreaker], descending: false, thenBy: true);
        }

        var items = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<TDto>(items, request.Page, request.PageSize, total);
    }

    private static IOrderedQueryable<T> Order<T>(IQueryable<T> query, LambdaExpression key, bool descending, bool thenBy)
    {
        var method = (thenBy, descending) switch
        {
            (false, false) => nameof(Queryable.OrderBy),
            (false, true) => nameof(Queryable.OrderByDescending),
            (true, false) => nameof(Queryable.ThenBy),
            (true, true) => nameof(Queryable.ThenByDescending),
        };

        var call = Expression.Call(typeof(Queryable), method, [typeof(T), key.ReturnType], query.Expression, Expression.Quote(key));
        return (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(call);
    }
}
