using System.Text;

namespace Merp.Application.Persistence;

/// <summary>
/// Case-insensitive "contains" search usable in LINQ queries of module DbContexts, which translate
/// <see cref="Matches"/> to PostgreSQL <c>ILIKE</c>. Keeps application code free of provider specific functions.
/// <code>
/// var pattern = TextSearch.ContainsPattern(search);
/// query.Where(c => TextSearch.Matches(c.Title, pattern) || TextSearch.Matches(c.Code, pattern));
/// </code>
/// </summary>
public static class TextSearch
{
    /// <summary>Translated to <c>value ILIKE pattern</c>; cannot run in memory.</summary>
    public static bool Matches(string? value, string pattern) =>
        throw new NotSupportedException($"{nameof(TextSearch)}.{nameof(Matches)} can only be used in database queries.");

    /// <summary><c>%term%</c> with the LIKE wildcards of <paramref name="term"/> escaped.</summary>
    public static string ContainsPattern(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        var builder = new StringBuilder(term.Length + 2).Append('%');
        foreach (var c in term.Trim())
        {
            if (c is '%' or '_' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.Append('%').ToString();
    }
}
