using System.Globalization;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Application.Imports;

/// <summary>
/// Validates and commits import jobs (D35), right after the upload or in the background. Each step is its own
/// command, so the job's state and every batch commit separately: a failed batch rolls back alone.
/// </summary>
public sealed partial class ImportRunner(
    IPlatformDbContext db,
    ImportRegistry registry,
    ImportReader reader,
    IFileStore files,
    ISpreadsheet spreadsheet,
    ISender sender,
    ILogger<ImportRunner> logger)
{
    public async Task ValidateAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var (job, definition) = await LoadAsync(jobId, cancellationToken);
        if (job.Status == ImportStatus.Queued)
        {
            await SendAsync(new StartImportValidationCommand(jobId), cancellationToken);
        }

        try
        {
            var sheet = await ReadAsync(job, definition, cancellationToken);
            var errors = sheet.Errors.ToList();
            if (sheet.Rows.Count > 0)
            {
                errors.AddRange(await definition.ValidateAsync(sheet.Rows, new ImportContext(job.UserId, job.Culture), cancellationToken));
            }

            if (sheet.Rows.Count == 0 && errors.Count == 0)
            {
                errors.Add(new ImportError(0, null, reader.Message("platform.imports.errors.empty", job.Culture)));
            }

            await SendAsync(new RecordImportValidationCommand(jobId, ToJobErrors(errors)), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, jobId);
            await SendAsync(new FailImportCommand(jobId, reader.Message("platform.imports.errors.unexpected", job.Culture)), cancellationToken);
        }
    }

    public async Task CommitAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var (job, definition) = await LoadAsync(jobId, cancellationToken);
        try
        {
            var sheet = await ReadAsync(job, definition, cancellationToken);
            var committed = 0;
            var errors = new List<ImportJobError>();
            foreach (var batch in sheet.Rows.Chunk(Math.Max(1, definition.BatchSize)))
            {
                var result = await sender.Send(definition.CreateBatch(batch), cancellationToken);
                if (result.IsSuccess)
                {
                    committed += batch.Length;
                    continue;
                }

                errors.AddRange(BatchErrors(batch, result.Error, job.Culture));
            }

            await SendAsync(new RecordImportCommitCommand(jobId, committed, errors), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, jobId);
            await SendAsync(new FailImportCommand(jobId, reader.Message("platform.imports.errors.unexpected", job.Culture)), cancellationToken);
        }
    }

    /// <summary>
    /// The rows of a batch that rolled back: their own errors (fields <c>{row}</c> or <c>{row}.{column}</c>), the others
    /// were not written because of them.
    /// </summary>
    private List<ImportJobError> BatchErrors(ImportRow[] batch, SharedKernel.Results.Error error, string culture)
    {
        var errors = new List<ImportJobError>();
        foreach (var field in error.Fields)
        {
            var parts = field.Field.Split('.', 2);
            if (int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var row))
            {
                errors.Add(new ImportJobError(row, parts.Length > 1 ? parts[1] : null, reader.Message(field.Code, culture, field.Args)));
            }
        }

        var failed = errors.Select(e => e.Row).ToHashSet();
        var reason = errors.Count == 0
            ? reader.Message(error.Code, culture, error.Args)
            : reader.Message("platform.imports.errors.batch_rolled_back", culture);
        errors.AddRange(batch.Where(r => !failed.Contains(r.Number)).Select(r => new ImportJobError(r.Number, null, reason)));
        return errors;
    }

    private async Task<(ImportJob Job, IImportDefinition Definition)> LoadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.AsNoTracking().SingleAsync(j => j.Id == ImportJobId.From(jobId), cancellationToken);
        var definition = registry.Find(job.ImportKey)
            ?? throw new InvalidOperationException($"Import {job.ImportKey} is not registered.");
        return (job, definition);
    }

    private async Task<ImportSheet> ReadAsync(ImportJob job, IImportDefinition definition, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == job.FileId, cancellationToken);
        await using var content = await files.OpenReadAsync(file, cancellationToken);
        var cells = spreadsheet.Read(content, int.MaxValue)
            ?? throw new InvalidOperationException($"The file of import job {job.Id.Value} cannot be read.");
        return reader.Read(cells, definition.Columns, job.Culture);
    }

    private async Task SendAsync(ICommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{command.GetType().Name} failed: {result.Error.Code}");
        }
    }

    private static List<ImportJobError> ToJobErrors(IEnumerable<ImportError> errors) =>
        [.. errors.OrderBy(e => e.Row).Select(e => new ImportJobError(e.Row, e.Column, e.Message))];

    [LoggerMessage(Level = LogLevel.Error, Message = "Import job {JobId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid jobId);
}
