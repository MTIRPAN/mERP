using System.Diagnostics;
using System.Text.Json;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Exports;

public sealed class ExportSettings
{
    public const string SectionName = "Exports";

    /// <summary>Rows an export may have; a larger list is refused, the user narrows the filter (D35).</summary>
    public int MaxRows { get; set; } = 10_000;

    /// <summary>Moments are written in this time zone; also the date in the file name.</summary>
    public string TimeZoneId { get; set; } = "Europe/Istanbul";
}

/// <summary>
/// Reads a list page by page through its query (data scope, filters and masking included), writes the chosen columns
/// to Excel and records the export in the audit log of the entity's module (D35).
/// </summary>
internal sealed class ListExporter(
    ISender sender,
    ISpreadsheet spreadsheet,
    ITextLocalizer localizer,
    ICurrentUser currentUser,
    IAuditWriter audit,
    TimeProvider timeProvider,
    IOptions<ExportSettings> options) : IListExporter
{
    public const string FiltersField = "filter";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<ExportFile>> ExportAsync<TItem>(ExportList<TItem> list, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        var settings = options.Value;
        var columns = Select(list.Columns, list.SelectedColumns);

        var items = new List<TItem>();
        for (var page = 1; ; page++)
        {
            var result = await sender.Send(list.Query(new PageRequest(page, PageRequest.MaxPageSize, list.Sort)), cancellationToken);
            if (result.IsFailure)
            {
                return result.Error;
            }

            if (result.Value.TotalCount > settings.MaxRows)
            {
                return Error.Validation([new FieldError(FiltersField, ExportRules.TooManyRowsCode, new Dictionary<string, object?>
                {
                    ["count"] = result.Value.TotalCount,
                    ["max"] = settings.MaxRows,
                })]);
            }

            items.AddRange(result.Value.Items);
            if (result.Value.Items.Count < PageRequest.MaxPageSize || items.Count >= result.Value.TotalCount)
            {
                break;
            }
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var name = localizer.Get(list.NameKey);
        var content = spreadsheet.WriteTable(
            name,
            [.. columns.Select(c => c.Header ?? localizer.Get(c.HeaderKey!))],
            items.Select(item => (IReadOnlyList<object?>)[.. columns.Select(c => Cell(c, item, zone, labels))]));

        await audit.WriteAsync(list.EntityType, [new AuditEntry
        {
            OccurredAt = timeProvider.GetUtcNow(),
            UserId = currentUser.UserId,
            EntityType = list.EntityType.Name,
            EntityId = list.EntityType.Name,
            Action = AuditAction.Export,
            NewValue = JsonSerializer.Serialize(new ExportRecord(items.Count, [.. columns.Select(c => c.Key)], list.Sort, Filters(list.Filters)), Json),
            CorrelationId = Activity.Current?.TraceId.ToString(),
        }], cancellationToken);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
        return new ExportFile($"{name}-{today:yyyy-MM-dd}.xlsx", content);
    }

    /// <summary>The chosen columns in the chosen order; keys the list does not offer (e.g. actions) are skipped.</summary>
    private static List<ExportColumn<TItem>> Select<TItem>(IReadOnlyList<ExportColumn<TItem>> columns, IReadOnlyList<string>? keys)
    {
        var chosen = (keys ?? [])
            .Select(key => columns.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal)))
            .OfType<ExportColumn<TItem>>()
            .DistinctBy(c => c.Key)
            .ToList();
        return chosen.Count > 0 ? chosen : [.. columns];
    }

    private object? Cell<TItem>(ExportColumn<TItem> column, TItem item, TimeZoneInfo zone, Dictionary<string, string> labels)
    {
        var value = column.Value(item);
        if (value is null)
        {
            return null;
        }

        if (column.LabelPrefix is { } prefix)
        {
            return Label(prefix + value, labels);
        }

        return value switch
        {
            bool flag => Label(flag ? "common.yes" : "common.no", labels),
            Enum => value.ToString(),
            DateTimeOffset moment => TimeZoneInfo.ConvertTime(moment, zone).DateTime,
            Guid id => id.ToString(),
            IEnumerable<string> texts => string.Join(", ", texts),
            _ => value,
        };
    }

    /// <summary>Texts repeat on every row: each is looked up once.</summary>
    private string Label(string key, Dictionary<string, string> labels)
    {
        if (!labels.TryGetValue(key, out var text))
        {
            text = localizer.Get(key);
            labels[key] = text;
        }

        return text;
    }

    private static Dictionary<string, string> Filters(IReadOnlyDictionary<string, object?> filters) =>
        filters.Where(f => f.Value is not null && f.Value is not string { Length: 0 })
            .ToDictionary(f => f.Key, f => Convert.ToString(f.Value, System.Globalization.CultureInfo.InvariantCulture)!, StringComparer.Ordinal);

    private sealed record ExportRecord(int Rows, IReadOnlyList<string> Columns, string? Sort, IReadOnlyDictionary<string, string> Filters);
}
