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

    public byte[] WriteTable(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName(sheetName));
        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        var row = 2;
        foreach (var values in rows)
        {
            for (var column = 0; column < values.Count; column++)
            {
                SetTyped(sheet.Cell(row, column + 1), values[column]);
            }

            row++;
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        if (headers.Count > 0)
        {
            sheet.Range(1, 1, Math.Max(1, row - 1), headers.Count).SetAutoFilter();

            // Widths from the first rows: measuring every cell of a large list is slow
            sheet.Columns(1, headers.Count).AdjustToContents(1, Math.Min(row - 1, 200), 8, 60);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetTyped(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                // Text stays text, so codes such as 0123 are not turned into numbers
                cell.SetValue(text);
                break;
            case bool flag:
                cell.SetValue(flag);
                break;
            case DateOnly date:
                cell.SetValue(date.ToDateTime(TimeOnly.MinValue));
                cell.Style.NumberFormat.Format = "dd.mm.yyyy";
                break;
            case DateTime moment:
                cell.SetValue(moment);
                cell.Style.NumberFormat.Format = "dd.mm.yyyy hh:mm";
                break;
            case decimal number:
                cell.SetValue(number);
                break;
            case int or long or short or double or float:
                cell.SetValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            default:
                cell.SetValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>Excel allows 31 characters and no <c>[]:*?/\</c> in sheet names.</summary>
    private static string SheetName(string name)
    {
        var clean = new string([.. name.Where(c => "[]:*?/\\".IndexOf(c, StringComparison.Ordinal) < 0)]).Trim();
        clean = clean.Length > 31 ? clean[..31] : clean;
        return clean.Length == 0 ? "Sheet1" : clean;
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
