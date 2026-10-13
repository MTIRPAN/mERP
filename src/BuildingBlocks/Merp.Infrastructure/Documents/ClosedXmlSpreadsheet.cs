using System.Globalization;
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

    public byte[] Write(
        string sheetName,
        IReadOnlyList<SpreadsheetColumn> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        IReadOnlyList<SpreadsheetNote>? notes = null,
        int listRows = 1000)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);
        IXLWorksheet? lists = null;
        for (var column = 0; column < columns.Count; column++)
        {
            var header = sheet.Cell(1, column + 1);
            header.SetValue(columns[column].Header);
            if (columns[column].Comment is { Length: > 0 } comment)
            {
                header.CreateComment().AddText(comment);
            }

            if (columns[column].Choices is { Count: > 0 } choices)
            {
                // Choices live on a hidden sheet: an inline list is limited to 255 characters
                lists ??= workbook.AddWorksheet("Lists");
                for (var i = 0; i < choices.Count; i++)
                {
                    lists.Cell(i + 1, column + 1).SetValue(choices[i]);
                }

                var validation = sheet.Range(2, column + 1, listRows + 1, column + 1).CreateDataValidation();
                validation.List(lists.Range(1, column + 1, choices.Count, column + 1), true);
            }
        }

        var row = 2;
        foreach (var values in rows)
        {
            for (var column = 0; column < values.Count; column++)
            {
                sheet.Cell(row, column + 1).SetValue(values[column] ?? string.Empty);
            }

            row++;
        }

        foreach (var note in notes ?? [])
        {
            var cell = sheet.Cell(note.Row + 2, note.Column + 1);
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FDE2E1");
            cell.CreateComment().AddText(note.Text);
        }

        lists?.Hide();
        var headerRow = sheet.Row(1);
        headerRow.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, Math.Max(1, columns.Count)).Width = 24;

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
                .Select(c => Text(r.Cell(c)))])];
        }
    }

    /// <summary>
    /// A cell as text that does not depend on the culture or the cell format: numbers in the invariant culture,
    /// dates as yyyy-MM-dd (with the time when it has one), booleans as true/false.
    /// </summary>
    private static string? Text(IXLCell cell)
    {
        var value = cell.Value;
        var text = value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Number => value.GetNumber().ToString("0.##########", CultureInfo.InvariantCulture),
            XLDataType.DateTime => value.GetDateTime() is var date && date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.Text => value.GetText(),
            _ => cell.GetFormattedString(),
        };
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
