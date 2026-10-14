namespace Merp.Application.Abstractions.Documents;

/// <summary>Excel (xlsx) files for exports and imports (D18, ClosedXML).</summary>
public interface ISpreadsheet
{
    /// <summary>One worksheet with a header row; cells are written as text.</summary>
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows);

    /// <summary>
    /// One worksheet with a header row (D35): header comments explain the columns, columns with choices get a
    /// drop-down list for <paramref name="listRows"/> rows, and notes mark cells with a comment and a red fill.
    /// </summary>
    byte[] Write(
        string sheetName,
        IReadOnlyList<SpreadsheetColumn> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        IReadOnlyList<SpreadsheetNote>? notes = null,
        int listRows = 1000);

    /// <summary>
    /// The rows of the first worksheet, the header row included, as text; empty cells are null. Numbers are in the
    /// invariant culture and dates are yyyy-MM-dd, whatever the cell format. Null when the content is not an xlsx file
    /// or has more than <paramref name="maxRows"/> rows.
    /// </summary>
    IReadOnlyList<IReadOnlyList<string?>>? Read(Stream content, int maxRows);
}

/// <param name="Header">Text of the header cell.</param>
/// <param name="Comment">Explanation shown on the header cell.</param>
/// <param name="Choices">Values offered in a drop-down list of the column's cells.</param>
public sealed record SpreadsheetColumn(string Header, string? Comment = null, IReadOnlyList<string>? Choices = null);

/// <param name="Row">0-based index of the data row (the header is not counted).</param>
/// <param name="Column">0-based column index.</param>
/// <param name="Text">Comment on the cell.</param>
public sealed record SpreadsheetNote(int Row, int Column, string Text);
