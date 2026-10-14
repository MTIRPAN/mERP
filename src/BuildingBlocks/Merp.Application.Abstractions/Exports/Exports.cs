using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Exports;

/// <summary>
/// A column of an exported list (D35). The value is read from the list item, which the list query has already
/// limited to the caller's data scope and masked where the caller may not see a field.
/// </summary>
public sealed class ExportColumn<TItem>
{
    private ExportColumn(string key, string? headerKey, string? header, Func<TItem, object?> value, string? labelPrefix)
    {
        Key = key;
        HeaderKey = headerKey;
        Header = header;
        Value = value;
        LabelPrefix = labelPrefix;
    }

    /// <summary>The column's key, the same as the list column's key on the screen.</summary>
    public string Key { get; }

    public string? HeaderKey { get; }

    /// <summary>Header text, for columns that are not defined in code (custom fields).</summary>
    public string? Header { get; }

    public Func<TItem, object?> Value { get; }

    /// <summary>The value is shown as the text of <c>{prefix}{value}</c>, e.g. web.platform.branch_type.Factory.</summary>
    public string? LabelPrefix { get; }

    /// <param name="key">The list column's key.</param>
    /// <param name="headerKey">Text key of the header.</param>
    /// <param name="value">The cell's value: text, a number, a date, a moment or a boolean.</param>
    /// <param name="labelPrefix">Shows the value as the text of this prefix and the value.</param>
    public static ExportColumn<TItem> Of(string key, string headerKey, Func<TItem, object?> value, string? labelPrefix = null) =>
        new(key, headerKey, null, value, labelPrefix);

    /// <summary>A column with a header text that is already in the user's language.</summary>
    public static ExportColumn<TItem> Named(string key, string header, Func<TItem, object?> value) =>
        new(key, null, header, value, null);
}

/// <summary>
/// A list to export: its columns and its list query. The rows are read page by page through the query, so the data
/// scope, the filters and the masking of the screen apply to the file too.
/// </summary>
/// <param name="NameKey">Text key of the list's name: the sheet and the file are named after it.</param>
/// <param name="EntityType">The entity of the rows; the export is audited under it.</param>
/// <param name="Columns">Every column the list offers.</param>
/// <param name="Query">The list query of a page.</param>
/// <param name="Sort">The sort of the screen.</param>
/// <param name="SelectedColumns">Keys of the columns to export, in this order; unknown keys are skipped, none means all.</param>
/// <param name="Filters">The screen's filters, recorded in the audit entry.</param>
public sealed record ExportList<TItem>(
    string NameKey,
    Type EntityType,
    IReadOnlyList<ExportColumn<TItem>> Columns,
    Func<PageRequest, IQuery<PagedResult<TItem>>> Query,
    string? Sort,
    IReadOnlyList<string>? SelectedColumns,
    IReadOnlyDictionary<string, object?> Filters);

/// <summary>An exported file.</summary>
public sealed record ExportFile(string FileName, byte[] Content)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>Exports lists to Excel (D35) and records each export in the audit log.</summary>
public interface IListExporter
{
    Task<Result<ExportFile>> ExportAsync<TItem>(ExportList<TItem> list, CancellationToken cancellationToken = default);
}

public static class ExportRules
{
    /// <summary>More rows than an export may have; the user narrows the filter.</summary>
    public const string TooManyRowsCode = "common.exports.errors.too_many_rows";

    /// <summary>Splits the <c>columns</c> query parameter (<c>code,name</c>).</summary>
    public static IReadOnlyList<string>? ParseColumns(string? columns) =>
        string.IsNullOrWhiteSpace(columns) ? null : columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
