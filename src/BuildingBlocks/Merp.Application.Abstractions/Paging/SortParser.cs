namespace Merp.Application.Abstractions.Paging;

internal static class SortParser
{
    /// <summary><c>name</c> sorts ascending, <c>-name</c> descending.</summary>
    public static (string Field, bool Descending) Parse(string sort)
    {
        var trimmed = sort.Trim();
        return trimmed.StartsWith('-') ? (trimmed[1..], true) : (trimmed, false);
    }
}
