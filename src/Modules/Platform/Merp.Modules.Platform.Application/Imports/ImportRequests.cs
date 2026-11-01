using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Application.Files;
using Merp.Modules.Platform.Domain.Imports;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Imports;

// Excel imports of all modules (PLAN P11b, D35). Any signed-in user may use the endpoints; each import needs its
// definition's permission, and a user sees only their own import jobs.

/// <summary>The imports of all modules.</summary>
public sealed class ImportRegistry(IEnumerable<IImportDefinition> definitions)
{
    private readonly Dictionary<string, IImportDefinition> _definitions = definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<IImportDefinition> All => _definitions.Values;

    public IImportDefinition? Find(string? key) => key is not null && _definitions.TryGetValue(key, out var definition) ? definition : null;
}

/// <summary>Limits of imports.</summary>
public sealed class ImportSettings
{
    public const string SectionName = "Platform:Imports";

    /// <summary>Data rows a file may have.</summary>
    public int MaxRows { get; set; } = 10_000;

    /// <summary>Files with more rows are validated and committed in the background (PLAN P11).</summary>
    public int BackgroundThreshold { get; set; } = 1_000;

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}

public static class ImportErrors
{
    public const string NotSpreadsheetCode = "platform.imports.errors.not_spreadsheet";
    public const string TooManyRowsCode = "platform.imports.errors.too_many_rows";
    public const string UpdateNotSupportedCode = "platform.imports.errors.update_not_supported";
    public const string InvalidMappingCode = "platform.imports.errors.invalid_mapping";
    public static readonly Error NotReady = Error.Conflict("platform.imports.errors.not_ready");
}

/// <remarks>SupportsUpdate: rows of existing records may update them (D40).</remarks>
public sealed record ImportDefinitionItem(string Key, string Name, bool SupportsUpdate);

/// <summary>The imports the caller may use.</summary>
public sealed record GetImportDefinitionsQuery : IQuery<IReadOnlyList<ImportDefinitionItem>>;

/// <summary>A file to download.</summary>
public sealed record ImportFile(string FileName, byte[] Content)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>The template of an import: localized headers with explanations, lists for choice columns.</summary>
public sealed record GetImportTemplateQuery(string Key) : IQuery<ImportFile>;

/// <summary>
/// Uploads a file to import; it is validated at once, or in the background when large. Returns the job id. With
/// <see cref="ImportMode.CreateOrUpdate"/> rows of existing records update them (D40).
/// </summary>
public sealed record StartImportCommand(string Key, FileUpload? File, ImportMode Mode = ImportMode.Create) : ICommand<Guid>
{
    /// <summary>
    /// Where each column is in the file (column key → header index), as the user mapped it when the headers are not
    /// the template's (D62); null finds the columns by their headers.
    /// </summary>
    public IReadOnlyDictionary<string, int>? Mapping { get; init; }
}

/// <summary>
/// Writes a validated file without errors, or with <paramref name="SkipInvalidRows"/> the rows of a file without
/// errors (D40); at once, or in the background when large.
/// </summary>
public sealed record CommitImportCommand(Guid JobId, bool SkipInvalidRows = false) : ICommand;

/// <remarks>
/// Errors holds the first 200; ErrorCount all that were kept. UpdateRows are the rows without errors that update an
/// existing record; CanCommitValidRows tells that only some rows have errors, so the others may be committed (D40).
/// </remarks>
public sealed record ImportJobDetails(
    Guid Id,
    string ImportKey,
    string ImportName,
    string FileName,
    ImportStatus Status,
    ImportMode Mode,
    int TotalRows,
    int ErrorRows,
    int UpdateRows,
    bool CanCommitValidRows,
    int CommittedRows,
    int ErrorCount,
    IReadOnlyList<ImportJobError> Errors,
    IReadOnlyDictionary<string, string> ColumnNames,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>One of the caller's import jobs.</summary>
public sealed record GetImportJobQuery(Guid Id) : IQuery<ImportJobDetails>;

/// <summary>The caller's latest import jobs of an import (or all), newest first, without their errors.</summary>
public sealed record GetImportJobsQuery(string? Key) : IQuery<IReadOnlyList<ImportJobDetails>>;

/// <summary>The uploaded rows with every error on its cell, and an errors column (PLAN P11 acceptance).</summary>
public sealed record GetImportReportQuery(Guid Id) : IQuery<ImportFile>;

/// <summary>Background step: validation of a queued job begins.</summary>
public sealed record StartImportValidationCommand(Guid JobId) : ICommand;

/// <summary>Background step: the result of the validation.</summary>
public sealed record RecordImportValidationCommand(Guid JobId, IReadOnlyList<ImportJobError> Errors, int UpdateRows = 0) : ICommand;

/// <summary>Background step: the result of the commit.</summary>
public sealed record RecordImportCommitCommand(Guid JobId, int CommittedRows, IReadOnlyList<ImportJobError> Errors) : ICommand;

/// <summary>Background step: the job stopped on an unexpected error.</summary>
public sealed record FailImportCommand(Guid JobId, string Message) : ICommand;

internal static class ImportJobs
{
    public static ImportJobDetails ToDetails(ImportJob job, ImportRegistry registry, ITextLocalizer localizer, bool withErrors)
    {
        var definition = registry.Find(job.ImportKey);
        return new(
            job.Id.Value,
            job.ImportKey,
            definition is null ? job.ImportKey : localizer.Get(definition.NameKey),
            job.FileName,
            job.Status,
            job.UpdatesExisting ? ImportMode.CreateOrUpdate : ImportMode.Create,
            job.TotalRows,
            job.ErrorRows,
            job.UpdateRows,
            job.CanCommitValidRows,
            job.CommittedRows,
            job.Errors.Count,
            withErrors ? [.. job.Errors.Take(200)] : [],
            withErrors && definition is not null
                ? definition.Columns.ToDictionary(c => c.Key, c => localizer.Get(c.HeaderKey), StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            job.CreatedAt,
            job.CompletedAt);
    }

    /// <summary>The caller's job; another user's is not found.</summary>
    public static Task<ImportJob?> FindOwnAsync(IPlatformDbContext db, ICurrentUser currentUser, Guid id, CancellationToken cancellationToken) =>
        db.ImportJobs.SingleOrDefaultAsync(j => j.Id == ImportJobId.From(id) && j.UserId == currentUser.UserId, cancellationToken);
}

internal sealed class GetImportDefinitionsQueryHandler(ImportRegistry registry, IPermissionChecker permissions, ITextLocalizer localizer)
    : IQueryHandler<GetImportDefinitionsQuery, IReadOnlyList<ImportDefinitionItem>>
{
    public async Task<Result<IReadOnlyList<ImportDefinitionItem>>> Handle(GetImportDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var items = new List<ImportDefinitionItem>();
        foreach (var definition in registry.All)
        {
            if (await permissions.HasPermissionAsync(definition.Permission, cancellationToken))
            {
                items.Add(new ImportDefinitionItem(definition.Key, localizer.Get(definition.NameKey), definition.SupportsUpdate));
            }
        }

        return items;
    }
}

internal sealed class GetImportTemplateQueryHandler(
    ImportRegistry registry,
    IPermissionChecker permissions,
    ImportReader reader,
    ISpreadsheet spreadsheet,
    ICurrentUser currentUser,
    ITextLocalizer localizer) : IQueryHandler<GetImportTemplateQuery, ImportFile>
{
    public async Task<Result<ImportFile>> Handle(GetImportTemplateQuery request, CancellationToken cancellationToken)
    {
        if (registry.Find(request.Key) is not { } definition)
        {
            return CommonErrors.NotFound;
        }

        if (!await permissions.HasPermissionAsync(definition.Permission, cancellationToken))
        {
            return CommonErrors.Forbidden;
        }

        var culture = currentUser.Culture;
        var columns = definition.Columns.Select(c => new SpreadsheetColumn(
            reader.Header(c, culture) + (c.Required ? " *" : string.Empty),
            c.HintKey is null ? null : localizer.Get(c.HintKey, culture: culture),
            c.Type switch
            {
                ImportColumnType.Choice => [.. (c.Choices ?? []).Select(choice => reader.Label(choice, culture))],
                ImportColumnType.Boolean => [localizer.Get("web.common.yes", culture: culture), localizer.Get("web.common.no", culture: culture)],
                _ => null,
            })).ToList();
        var content = spreadsheet.Write(Sheet(localizer.Get(definition.NameKey, culture: culture)), columns, []);
        return new ImportFile($"{definition.Key}.xlsx", content);
    }

    /// <summary>Excel allows 31 characters and no <c>[]:*?/\</c> in sheet names.</summary>
    internal static string Sheet(string name)
    {
        var clean = new string([.. name.Where(c => "[]:*?/\\".IndexOf(c, StringComparison.Ordinal) < 0)]);
        return clean.Length > 31 ? clean[..31] : clean;
    }
}

internal sealed class StartImportCommandValidator : AbstractValidator<StartImportCommand>
{
    public StartImportCommandValidator()
    {
        RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
        RuleFor(c => c.Mode).IsInEnum();
        RuleFor(c => c.Mapping)
            .Must(m => m!.Values.All(i => i is >= 0 and < SuggestImportMappingQuery.MaxHeaders) && m.Values.Distinct().Count() == m.Count)
            .WithErrorCode(ImportErrors.InvalidMappingCode)
            .When(c => c.Mapping is not null);
    }
}

internal sealed class StartImportCommandHandler(
    IPlatformDbContext db,
    ImportRegistry registry,
    IPermissionChecker permissions,
    IFileStore files,
    ISpreadsheet spreadsheet,
    ImportSettings settings,
    ICurrentUser currentUser,
    IAfterCommit afterCommit,
    IBackgroundTasks background,
    ImportRunner runner) : ICommandHandler<StartImportCommand, Guid>
{
    public async Task<Result<Guid>> Handle(StartImportCommand request, CancellationToken cancellationToken)
    {
        if (registry.Find(request.Key) is not { } definition)
        {
            return CommonErrors.NotFound;
        }

        if (currentUser.UserId is not { } userId || !await permissions.HasPermissionAsync(definition.Permission, cancellationToken))
        {
            return CommonErrors.Forbidden;
        }

        if (request.Mode == ImportMode.CreateOrUpdate && !definition.SupportsUpdate)
        {
            return Error.Validation([new FieldError("mode", ImportErrors.UpdateNotSupportedCode)]);
        }

        if (request.Mapping is { } mapping && mapping.Keys.Any(k => !definition.Columns.Any(c => string.Equals(c.Key, k, StringComparison.Ordinal))))
        {
            return Error.Validation([new FieldError("mapping", ImportErrors.InvalidMappingCode)]);
        }

        var jobId = Guid.CreateVersion7();
        var stored = await files.SaveAsync(
            PlatformFileOwners.Import,
            jobId,
            StoredFilePurposes.Import,
            request.File!,
            new FileRules(new HashSet<string>(["xlsx"], StringComparer.Ordinal), settings.MaxBytes),
            cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        // Counted here, so a file too large is refused at once and a large one goes to the background
        await using var content = await files.OpenReadAsync(stored.Value, cancellationToken);
        if (spreadsheet.Read(content, int.MaxValue) is not { } cells)
        {
            return Error.Validation([new FieldError(FileErrors.Field, ImportErrors.NotSpreadsheetCode)]);
        }

        var rows = cells.Skip(1).Count(r => !r.All(string.IsNullOrWhiteSpace));
        if (rows > settings.MaxRows)
        {
            return Error.Validation([new FieldError(FileErrors.Field, ImportErrors.TooManyRowsCode, new Dictionary<string, object?> { ["max"] = settings.MaxRows })]);
        }

        var queued = rows > settings.BackgroundThreshold;
        var job = ImportJob.Create(
            ImportJobId.From(jobId),
            definition.Key,
            userId,
            currentUser.Culture,
            stored.Value.Id,
            stored.Value.FileName,
            rows,
            queued,
            request.Mode == ImportMode.CreateOrUpdate,
            request.Mapping);
        db.ImportJobs.Add(job);
        db.StoredFiles.Add(stored.Value);

        if (queued)
        {
            background.Enqueue<ImportRunner>(r => r.ValidateAsync(jobId, CancellationToken.None));
        }
        else
        {
            afterCommit.Enqueue(ct => runner.ValidateAsync(jobId, ct));
        }

        return jobId;
    }
}

internal sealed class CommitImportCommandHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ImportSettings settings,
    IAfterCommit afterCommit,
    IBackgroundTasks background,
    ImportRunner runner) : ICommandHandler<CommitImportCommand>
{
    public async Task<Result> Handle(CommitImportCommand request, CancellationToken cancellationToken)
    {
        var job = await ImportJobs.FindOwnAsync(db, currentUser, request.JobId, cancellationToken);
        if (job is null)
        {
            return CommonErrors.NotFound;
        }

        if (!job.StartCommit(request.SkipInvalidRows))
        {
            return ImportErrors.NotReady;
        }

        var jobId = request.JobId;
        if (job.TotalRows > settings.BackgroundThreshold)
        {
            background.Enqueue<ImportRunner>(r => r.CommitAsync(jobId, CancellationToken.None));
        }
        else
        {
            afterCommit.Enqueue(ct => runner.CommitAsync(jobId, ct));
        }

        return Result.Success();
    }
}

internal sealed class GetImportJobQueryHandler(IPlatformDbContext db, ICurrentUser currentUser, ImportRegistry registry, ITextLocalizer localizer)
    : IQueryHandler<GetImportJobQuery, ImportJobDetails>
{
    public async Task<Result<ImportJobDetails>> Handle(GetImportJobQuery request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.Id == ImportJobId.From(request.Id) && j.UserId == currentUser.UserId, cancellationToken);
        return job is null ? CommonErrors.NotFound : ImportJobs.ToDetails(job, registry, localizer, withErrors: true);
    }
}

internal sealed class GetImportJobsQueryHandler(IPlatformDbContext db, ICurrentUser currentUser, ImportRegistry registry, ITextLocalizer localizer)
    : IQueryHandler<GetImportJobsQuery, IReadOnlyList<ImportJobDetails>>
{
    public async Task<Result<IReadOnlyList<ImportJobDetails>>> Handle(GetImportJobsQuery request, CancellationToken cancellationToken)
    {
        var query = db.ImportJobs.AsNoTracking().Where(j => j.UserId == currentUser.UserId);
        if (!string.IsNullOrWhiteSpace(request.Key))
        {
            query = query.Where(j => j.ImportKey == request.Key);
        }

        var jobs = await query.OrderByDescending(j => j.CreatedAt).Take(20).ToListAsync(cancellationToken);
        return jobs.Select(j => ImportJobs.ToDetails(j, registry, localizer, withErrors: false)).ToList();
    }
}

internal sealed class GetImportReportQueryHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ImportRegistry registry,
    IFileStore files,
    ISpreadsheet spreadsheet,
    ITextLocalizer localizer,
    ImportReader reader) : IQueryHandler<GetImportReportQuery, ImportFile>
{
    public async Task<Result<ImportFile>> Handle(GetImportReportQuery request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.Id == ImportJobId.From(request.Id) && j.UserId == currentUser.UserId, cancellationToken);
        var file = job is null ? null : await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == job.FileId, cancellationToken);
        if (job is null || file is null || registry.Find(job.ImportKey) is not { } definition)
        {
            return CommonErrors.NotFound;
        }

        await using var content = await files.OpenReadAsync(file, cancellationToken);
        var cells = spreadsheet.Read(content, int.MaxValue) ?? [];
        var headers = cells.Count > 0 ? cells[0] : [];
        var columns = headers.Select(h => new SpreadsheetColumn(h ?? string.Empty)).ToList();
        var errorsHeader = localizer.Get("platform.imports.report.errors", culture: job.Culture);
        columns.Add(new SpreadsheetColumn(errorsHeader));

        // Where each column of the definition is in the file, to put a cell's error on its cell
        var positions = reader.Positions(headers, definition.Columns, job.Culture, job.ColumnMapping);

        var byRow = job.Errors.Where(e => e.Row > 1).GroupBy(e => e.Row).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<IReadOnlyList<string?>>();
        var notes = new List<SpreadsheetNote>();
        for (var r = 1; r < cells.Count; r++)
        {
            var line = cells[r].Concat(Enumerable.Repeat<string?>(null, Math.Max(0, headers.Count - cells[r].Count))).Take(headers.Count).ToList();
            var errors = byRow.GetValueOrDefault(r + 1) ?? [];
            line.Add(errors.Count == 0 ? null : string.Join(" | ", errors.Select(e => e.Message)));
            rows.Add(line);
            foreach (var error in errors)
            {
                var column = error.Column is not null && positions.TryGetValue(error.Column, out var at) ? at : headers.Count;
                notes.Add(new SpreadsheetNote(r - 1, column, error.Message));
            }
        }

        var name = Path.GetFileNameWithoutExtension(job.FileName);
        return new ImportFile($"{name}-{localizer.Get("platform.imports.report.suffix", culture: job.Culture)}.xlsx", spreadsheet.Write(GetImportTemplateQueryHandler.Sheet(name), columns, rows, notes));
    }
}

internal sealed class StartImportValidationCommandHandler(IPlatformDbContext db) : ICommandHandler<StartImportValidationCommand>
{
    public async Task<Result> Handle(StartImportValidationCommand request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.SingleAsync(j => j.Id == ImportJobId.From(request.JobId), cancellationToken);
        job.StartValidation();
        return Result.Success();
    }
}

internal sealed class RecordImportValidationCommandHandler(IPlatformDbContext db) : ICommandHandler<RecordImportValidationCommand>
{
    public async Task<Result> Handle(RecordImportValidationCommand request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.SingleAsync(j => j.Id == ImportJobId.From(request.JobId), cancellationToken);
        job.Validated(request.Errors, request.UpdateRows);
        return Result.Success();
    }
}

internal sealed class RecordImportCommitCommandHandler(IPlatformDbContext db, TimeProvider timeProvider) : ICommandHandler<RecordImportCommitCommand>
{
    public async Task<Result> Handle(RecordImportCommitCommand request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.SingleAsync(j => j.Id == ImportJobId.From(request.JobId), cancellationToken);
        job.Committed(request.CommittedRows, request.Errors, timeProvider.GetUtcNow());
        return Result.Success();
    }
}

internal sealed class FailImportCommandHandler(IPlatformDbContext db, TimeProvider timeProvider) : ICommandHandler<FailImportCommand>
{
    public async Task<Result> Handle(FailImportCommand request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.SingleAsync(j => j.Id == ImportJobId.From(request.JobId), cancellationToken);
        job.Fail(request.Message, timeProvider.GetUtcNow());
        return Result.Success();
    }
}
