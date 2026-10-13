using System.Text.Json.Serialization;
using Merp.Application.Abstractions.Messaging;

namespace Merp.Application.Abstractions.Imports;

/// <summary>How a cell is read (D35).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportColumnType>))]
public enum ImportColumnType
{
    Text,

    /// <summary>A decimal; a comma or a point separates the fraction.</summary>
    Number,

    WholeNumber,

    /// <summary><c>gg.aa.yyyy</c> or <c>yyyy-aa-gg</c>.</summary>
    Date,

    /// <summary>Evet/Hayır, true/false or 1/0.</summary>
    Boolean,

    /// <summary>One of <see cref="ImportColumn.Choices"/>, by its label or its value; the template offers a list.</summary>
    Choice,
}

/// <param name="Value">Value the import gets, e.g. <c>Factory</c>.</param>
/// <param name="LabelKey">Localization key of the label in the template's list.</param>
public sealed record ImportChoice(string Value, string LabelKey);

/// <param name="Key">Key of the value in <see cref="ImportRow"/>, e.g. <c>companyCode</c>.</param>
/// <param name="HeaderKey">Localization key of the header in the template.</param>
/// <param name="Type">How the cell is read.</param>
/// <param name="Required">An empty cell is an error.</param>
/// <param name="HintKey">Localization key of the explanation on the header.</param>
/// <param name="Choices">Values of a <see cref="ImportColumnType.Choice"/> column.</param>
/// <param name="MaxLength">Longest text allowed.</param>
public sealed record ImportColumn(
    string Key,
    string HeaderKey,
    ImportColumnType Type = ImportColumnType.Text,
    bool Required = false,
    string? HintKey = null,
    IReadOnlyList<ImportChoice>? Choices = null,
    int? MaxLength = null);

/// <summary>A data row read from the file: its row number in the sheet and the typed values by column key.</summary>
public sealed class ImportRow(int number, IReadOnlyDictionary<string, object?> values)
{
    /// <summary>Row number in the sheet, the header being row 1.</summary>
    public int Number { get; } = number;

    public IReadOnlyDictionary<string, object?> Values { get; } = values;

    public string? Text(string key) => Values.GetValueOrDefault(key) as string;

    public decimal? AsDecimal(string key) => Values.GetValueOrDefault(key) as decimal?;

    public DateOnly? AsDate(string key) => Values.GetValueOrDefault(key) as DateOnly?;

    public bool? AsBoolean(string key) => Values.GetValueOrDefault(key) as bool?;
}

/// <summary>
/// Who imports, in which language. A large import is validated in the background as the system, so the user's data
/// scope is checked through their id, not the current caller.
/// </summary>
public sealed record ImportContext(Guid UserId, string Culture);

/// <summary>A problem of a row, at a column when it is one cell's; the message is in the user's language.</summary>
public sealed record ImportError(int Row, string? Column, string Message);

/// <summary>
/// An Excel import a module offers (D35), e.g. branches. Platform reads the file, checks the cells against the
/// columns, asks the definition to check the rows, and commits them in batches through the definition's
/// <see cref="IImportBatchCommand"/>, each batch in one transaction.
/// </summary>
public interface IImportDefinition
{
    /// <summary>Key, <c>{module}.{name}</c>, e.g. <c>platform.branches</c>.</summary>
    string Key { get; }

    /// <summary>Localization key of the import's name.</summary>
    string NameKey { get; }

    /// <summary>Permission needed to import.</summary>
    string Permission { get; }

    IReadOnlyList<ImportColumn> Columns { get; }

    /// <summary>Rows per transaction when committing.</summary>
    int BatchSize => 100;

    /// <summary>
    /// Checks rows whose cells are fine, as a whole: references to existing records, duplicates in the file or the
    /// database, the caller's data scope. Reads only. Should look records up once per call, not per row.
    /// </summary>
    Task<IReadOnlyList<ImportError>> ValidateAsync(IReadOnlyList<ImportRow> rows, ImportContext context, CancellationToken cancellationToken = default);

    /// <summary>The command that writes one batch; it fails as a whole, so its transaction rolls back.</summary>
    IImportBatchCommand CreateBatch(IReadOnlyList<ImportRow> rows);
}

/// <summary>
/// Writes a batch of an import in one transaction (D35). A failure names the rows in its field errors as
/// <c>{row}</c> or <c>{row}.{column}</c>.
/// </summary>
public interface IImportBatchCommand : ICommand, IBulkCommand;
