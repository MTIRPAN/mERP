using ClosedXML.Excel;
using Merp.Application.Abstractions.Documents;

namespace Merp.Infrastructure.Documents;

internal sealed class ClosedXmlSpreadsheet : ISpreadsheet
{
    public byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);
        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        var row = 2;
        foreach (var values in rows)
        {
            for (var column = 0; column < values.Count; column++)
            {
                // Text, so keys and codes are never turned into numbers or dates
                sheet.Cell(row, column + 1).SetValue(values[column] ?? string.Empty);
            }

            row++;
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, headers.Count).Width = 60;
        sheet.Columns(1, headers.Count).Style.Alignment.WrapText = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<IReadOnlyList<string?>>? Read(Stream content, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(content);
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(content);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or FormatException or IOException
                                              or InvalidOperationException or NotSupportedException
                                              or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
        {
            return null;
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet?.RangeUsed() is not { } used)
            {
                return [];
            }

            if (used.RowCount() > maxRows)
            {
                return null;
            }

            var columns = used.ColumnCount();
            return [.. used.Rows().Select(r => (IReadOnlyList<string?>)[.. Enumerable.Range(1, columns)
                .Select(c => r.Cell(c).GetFormattedString() is { Length: > 0 } text ? text : null)])];
        }
    }
}
