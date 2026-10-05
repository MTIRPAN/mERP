using System.Linq.Expressions;

namespace Merp.Application.Abstractions.Paging;

/// <summary>
/// Sortable fields of a list. Only declared fields can be sorted on; the tie breaker (usually the id) keeps pages
/// stable when values repeat.
/// <code>
/// SortMap&lt;Company&gt;.By(c => c.Id).Add("code", c => c.Code).Add("name", c => c.ShortName)
/// </code>
/// </summary>
public sealed class SortMap<T>
{
    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<IOrderedQueryable<T>, IOrderedQueryable<T>> _tieBreaker;

    private SortMap(Func<IOrderedQueryable<T>, IOrderedQueryable<T>> tieBreaker) => _tieBreaker = tieBreaker;

    /// <summary>A map whose orderings end with <paramref name="tieBreaker"/>.</summary>
    public static SortMap<T> By<TKey>(Expression<Func<T, TKey>> tieBreaker)
    {
        ArgumentNullException.ThrowIfNull(tieBreaker);
        return new SortMap<T>(query => query.ThenBy(tieBreaker));
    }

    public SortMap<T> Add<TKey>(string field, Expression<Func<T, TKey>> key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(key);
        _fields[field] = (query, descending) => descending ? query.OrderByDescending(key) : query.OrderBy(key);
        return this;
    }

    /// <summary>True for an empty sort or a declared field (with an optional leading <c>-</c>).</summary>
    public bool IsValid(string? sort) => string.IsNullOrWhiteSpace(sort) || _fields.ContainsKey(Field(sort));

    /// <summary>Sorts by <paramref name="sort"/>, or by <paramref name="defaultSort"/> when it is empty.</summary>
    public IOrderedQueryable<T> Apply(IQueryable<T> query, string? sort, string defaultSort)
    {
        ArgumentNullException.ThrowIfNull(query);
        var effective = string.IsNullOrWhiteSpace(sort) ? defaultSort : sort.Trim();
        if (!_fields.TryGetValue(Field(effective), out var order))
        {
            throw new ArgumentException($"'{effective}' is not a sortable field.", nameof(sort));
        }

        return _tieBreaker(order(query, effective.StartsWith('-')));
    }

    private static string Field(string sort) => sort.Trim().TrimStart('-');
}
