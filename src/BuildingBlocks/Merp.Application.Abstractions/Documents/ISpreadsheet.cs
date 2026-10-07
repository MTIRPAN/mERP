namespace Merp.Application.Abstractions.Documents;

/// <summary>Excel (xlsx) files for exports and imports (D18, ClosedXML).</summary>
public interface ISpreadsheet
{
    /// <summary>One worksheet with a header row; cells are written as text.</summary>
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows);

    /// <summary>
    /// The rows of the first worksheet, the header row included, as text; empty cells are null. Null when the content
    /// is not an xlsx file or has more than <paramref name="maxRows"/> rows.
    /// </summary>
    IReadOnlyList<IReadOnlyList<string?>>? Read(Stream content, int maxRows);
}
