using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;

namespace Merp.Modules.Platform.Domain.Imports;

public readonly record struct ImportJobId(Guid Value) : IEntityId<ImportJobId>
{
    public static ImportJobId From(Guid value) => new(value);
}

/// <summary>Where an import stands (D35): upload → validation → (fix and upload again) → commit.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportStatus>))]
public enum ImportStatus
{
    /// <summary>Uploaded; validation waits for the background job.</summary>
    Queued,

    Validating,

    /// <summary>Validated with errors; the file is fixed and uploaded again, or its rows without errors are committed (D40).</summary>
    Invalid,

    /// <summary>Validated without errors; waits for the user to commit.</summary>
    Ready,

    Committing,

    Completed,

    /// <summary>
    /// Some rows were not written: rows with errors left out on purpose (D40) or rows of batches that failed at commit;
    /// they are in the errors, the others were written.
    /// </summary>
    PartiallyCompleted,

    /// <summary>Validation or commit stopped on an unexpected error.</summary>
    Failed,
}

/// <param name="Row">Row number in the sheet (the header is row 1); 0 for the file as a whole.</param>
/// <param name="Column">Column key, or null for the row.</param>
/// <param name="Message">Text in the importing user's language.</param>
public sealed record ImportJobError(int Row, string? Column, string Message);

/// <summary>
/// One upload of an import (D35): the stored file, the counts and the errors. It is the record of who imported what;
/// the records it writes are audited themselves, so it is not audited again.
/// </summary>
public sealed class ImportJob : AuditableAggregateRoot<ImportJobId>
{
    public const int KeyMaxLength = 100;
    public const int FileNameMaxLength = 255;

    /// <summary>Errors kept for the preview and the report; a file with more is broken anyway.</summary>
    public const int MaxErrors = 1000;

    private ImportJob()
    {
    }

    /// <summary>The import definition, e.g. <c>platform.branches</c>.</summary>
    public string ImportKey { get; private set; } = null!;

    public Guid UserId { get; private set; }

    /// <summary>Culture of the user, for the headers and the messages.</summary>
    public string Culture { get; private set; } = null!;

    public Guid FileId { get; private set; }

    public string FileName { get; private set; } = null!;

    public ImportStatus Status { get; private set; }

    /// <summary>Data rows of the file (the header not counted).</summary>
    public int TotalRows { get; private set; }

    /// <summary>Rows with at least one error.</summary>
    public int ErrorRows { get; private set; }

    public int CommittedRows { get; private set; }

    /// <summary>Rows of existing records update them instead of being errors (D40).</summary>
    public bool UpdatesExisting { get; private set; }

    /// <summary>Rows without errors that update an existing record; the rest add one.</summary>
    public int UpdateRows { get; private set; }

    /// <summary>
    /// Validated with errors in some rows only: the rows without errors may be committed (D40). Not when the file
    /// as a whole is wrong, e.g. a missing column.
    /// </summary>
    public bool CanCommitValidRows => Status == ImportStatus.Invalid && ErrorRows < TotalRows && !Errors.Any(e => e.Row < 2);

    public IReadOnlyList<ImportJobError> Errors { get; private set; } = [];

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <param name="id">Known before the job is saved: the uploaded file is stored under it.</param>
    /// <param name="importKey">The import definition, e.g. platform.branches.</param>
    /// <param name="userId">Who uploaded the file; only they see the job.</param>
    /// <param name="culture">The language of the template headers and of the messages.</param>
    /// <param name="fileId">The uploaded file.</param>
    /// <param name="fileName">The uploaded file's name.</param>
    /// <param name="totalRows">Data rows of the file.</param>
    /// <param name="queued">Validated in the background, so waiting in the queue first.</param>
    /// <param name="updatesExisting">Rows of existing records update them (D40).</param>
    public static ImportJob Create(
        ImportJobId id,
        string importKey,
        Guid userId,
        string culture,
        Guid fileId,
        string fileName,
        int totalRows,
        bool queued,
        bool updatesExisting = false) => new()
    {
        Id = id,
        ImportKey = Text.Required(importKey, KeyMaxLength, nameof(importKey)),
        UserId = userId,
        Culture = culture,
        FileId = fileId,
        FileName = Text.Required(fileName, FileNameMaxLength, nameof(fileName)),
        TotalRows = totalRows,
        Status = queued ? ImportStatus.Queued : ImportStatus.Validating,
        UpdatesExisting = updatesExisting,
    };

    public void StartValidation() => Status = ImportStatus.Validating;

    /// <summary>The rows were checked: ready to commit, or invalid with the errors.</summary>
    /// <param name="errors">The errors of the rows and of the file.</param>
    /// <param name="updateRows">Rows without errors that would update an existing record.</param>
    public void Validated(IReadOnlyList<ImportJobError> errors, int updateRows = 0)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = [.. errors.Take(MaxErrors)];
        ErrorRows = errors.Select(e => e.Row).Distinct().Count();
        UpdateRows = updateRows;
        Status = errors.Count == 0 ? ImportStatus.Ready : ImportStatus.Invalid;
    }

    /// <summary>
    /// A validated file is committed once: without errors, or with <paramref name="skipInvalidRows"/> its rows without
    /// errors (D40).
    /// </summary>
    public bool StartCommit(bool skipInvalidRows = false)
    {
        if (Status != ImportStatus.Ready && !(skipInvalidRows && CanCommitValidRows))
        {
            return false;
        }

        Status = ImportStatus.Committing;
        return true;
    }

    public void Committed(int committedRows, IReadOnlyList<ImportJobError> errors, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(errors);
        CommittedRows = committedRows;
        Errors = [.. errors.Take(MaxErrors)];
        ErrorRows = errors.Select(e => e.Row).Distinct().Count();
        Status = errors.Count == 0 ? ImportStatus.Completed : ImportStatus.PartiallyCompleted;
        CompletedAt = now;
    }

    public void Fail(string message, DateTimeOffset now)
    {
        Errors = [new ImportJobError(0, null, message)];
        Status = ImportStatus.Failed;
        CompletedAt = now;
    }
}
