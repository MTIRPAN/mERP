namespace Merp.SharedKernel.Text;

/// <summary>
/// Accent and case insensitive matching for mixed Turkish and English text. Culture aware lower casing cannot do
/// this: tr-TR lowers "I" to "ı" (so "IBM" misses "ibm"), the invariant culture lowers "İ" to "i̇". Folding maps
/// every I/i variant to "i" and drops the Turkish diacritics, one character per character.
/// </summary>
public static class TurkishText
{
    /// <summary>Characters replaced by <see cref="Fold"/> and the SQL <c>translate</c> in <see cref="SqlFold"/>.</summary>
    public const string FoldFrom = "İIıŞşĞğÜüÖöÇçÂâÎîÛû";

    public const string FoldTo = "iiissgguuoocc" + "aaiiuu";

    /// <summary>Lower case, diacritic free form with exactly one character per input character.</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var index = FoldFrom.IndexOf(c, StringComparison.Ordinal);
                span[i] = index >= 0 ? FoldTo[index] : char.ToLowerInvariant(c);
            }
        });
    }

    /// <summary>
    /// PostgreSQL expression that folds <paramref name="sqlExpression"/> the same way as <see cref="Fold"/>, for
    /// generated search columns.
    /// </summary>
    public static string SqlFold(string sqlExpression) => $"lower(translate({sqlExpression}, '{FoldFrom}', '{FoldTo}'))";
}
