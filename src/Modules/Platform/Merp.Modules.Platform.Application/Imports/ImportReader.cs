using System.Globalization;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;

namespace Merp.Modules.Platform.Application.Imports;

/// <summary>The rows of a file with their cell errors (D35); rows with an error are not passed to the definition.</summary>
public sealed record ImportSheet(IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportError> Errors, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string?>> Cells);

/// <summary>
/// Reads an import sheet (D35): finds the columns by their header in the user's language, the default language or
/// the column key, and turns cells into typed values by the column type.
/// </summary>
public sealed class ImportReader(ITextLocalizer localizer)
{
    public const string DefaultCulture = "tr-TR";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    // Date cells are read as yyyy-MM-dd; dates typed as text are in the Turkish order
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy HH:mm"];
    private static readonly string[] Yes = ["evet", "e", "true", "1", "yes", "x"];
    private static readonly string[] No = ["hayır", "hayir", "h", "false", "0", "no"];

    /// <summary>Header text of a column in a culture.</summary>
    public string Header(ImportColumn column, string culture) => localizer.Get(column.HeaderKey, culture: culture);

    /// <summary>Label of a choice in a culture.</summary>
    public string Label(ImportChoice choice, string culture) => localizer.Get(choice.LabelKey, culture: culture);

    public string Message(string key, string culture, IReadOnlyDictionary<string, object?>? args = null) => localizer.Get(key, args, culture);

    public ImportSheet Read(IReadOnlyList<IReadOnlyList<string?>> cells, IReadOnlyList<ImportColumn> columns, string culture)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(columns);
        var errors = new List<ImportError>();
        if (cells.Count == 0)
        {
            return new ImportSheet([], [new ImportError(0, null, Message("platform.imports.errors.empty", culture))], [], []);
        }

        // The template marks required columns with an asterisk
        var headers = cells[0].Select(h => (h ?? string.Empty).Trim().TrimEnd('*').Trim()).ToList();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            string[] names = [Header(column, culture), Header(column, DefaultCulture), column.Key];
            var index = headers.FindIndex(h => names.Any(n => string.Equals(n, h, StringComparison.OrdinalIgnoreCase)));
            if (index >= 0)
            {
                positions[column.Key] = index;
            }
            else if (column.Required)
            {
                errors.Add(new ImportError(1, column.Key, Message("platform.imports.errors.missing_column", culture, new Dictionary<string, object?> { ["column"] = Header(column, culture) })));
            }
        }

        if (errors.Count > 0)
        {
            return new ImportSheet([], errors, headers, cells);
        }

        var rows = new List<ImportRow>();
        for (var r = 1; r < cells.Count; r++)
        {
            var line = cells[r];
            if (line.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var number = r + 1;
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var rowErrors = new List<ImportError>();
            foreach (var column in columns)
            {
                var raw = positions.TryGetValue(column.Key, out var at) && at < line.Count ? line[at]?.Trim() : null;
                var (value, error) = Parse(column, string.IsNullOrEmpty(raw) ? null : raw, culture);
                if (error is not null)
                {
                    rowErrors.Add(new ImportError(number, column.Key, error));
                }

                values[column.Key] = value;
            }

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
            }
            else
            {
                rows.Add(new ImportRow(number, values));
            }
        }

        return new ImportSheet(rows, errors, headers, cells);
    }

    private (object? Value, string? Error) Parse(ImportColumn column, string? raw, string culture)
    {
        if (raw is null)
        {
            return column.Required ? (null, Message("platform.imports.errors.required", culture)) : (null, null);
        }

        switch (column.Type)
        {
            case ImportColumnType.Number:
            case ImportColumnType.WholeNumber:
                // Number cells are read in the invariant culture; a number typed as text with a comma is Turkish (1.234,5)
                var parsed = raw.Contains(',', StringComparison.Ordinal)
                    ? decimal.TryParse(raw, NumberStyles.Number, Turkish, out var number)
                    : decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number)
                      || decimal.TryParse(raw, NumberStyles.Number, Turkish, out number);
                if (!parsed)
                {
                    return (null, Message("platform.imports.errors.invalid_number", culture));
                }

                return column.Type == ImportColumnType.WholeNumber && number != decimal.Truncate(number)
                    ? (null, Message("platform.imports.errors.invalid_whole_number", culture))
                    : (number, null);
            case ImportColumnType.Date:
                return DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? (DateOnly.FromDateTime(date), null)
                    : (null, Message("platform.imports.errors.invalid_date", culture));
            case ImportColumnType.Boolean:
                var flag = raw.ToLower(Turkish);
                return Yes.Contains(flag, StringComparer.Ordinal) ? (true, null)
                    : No.Contains(flag, StringComparer.Ordinal) ? (false, null)
                    : (null, Message("platform.imports.errors.invalid_boolean", culture));
            case ImportColumnType.Choice:
                var choice = (column.Choices ?? []).FirstOrDefault(c =>
                    string.Equals(c.Value, raw, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Label(c, culture), raw, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Label(c, DefaultCulture), raw, StringComparison.OrdinalIgnoreCase));
                return choice is not null
                    ? (choice.Value, null)
                    : (null, Message("platform.imports.errors.invalid_choice", culture, new Dictionary<string, object?>
                    {
                        ["choices"] = string.Join(", ", (column.Choices ?? []).Select(c => Label(c, culture))),
                    }));
            default:
                return column.MaxLength is { } max && raw.Length > max
                    ? (null, Message("common.errors.max_length", culture, new Dictionary<string, object?> { ["maxLength"] = max }))
                    : (raw, null);
        }
    }
}
