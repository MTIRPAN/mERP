using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Imports;

/// <summary>
/// The job catalog from Excel (PLAN H4, D40). The catalog is shared by the companies, so <c>hr.org.manage</c> must come
/// with the Tenant scope (D37).
/// </summary>
public sealed class JobImport(IHrDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, ITextLocalizer localizer) : IImportDefinition
{
    public const string ImportKey = "hr.jobs";

    public static class Columns
    {
        public const string Code = "code";
        public const string Name = "name";
        public const string Family = "family";
        public const string Level = "level";
    }

    public string Key => ImportKey;

    public string NameKey => "hr.imports.jobs.name";

    public string Permission => HrPermissions.Org.Manage;

    IReadOnlyList<ImportColumn> IImportDefinition.Columns { get; } =
    [
        new(Columns.Code, "hr.imports.jobs.code", Required: true, HintKey: "hr.imports.code_hint", MaxLength: Job.CodeMaxLength),
        new(Columns.Name, "hr.imports.jobs.job_name", Required: true, MaxLength: Job.NameMaxLength),
        new(Columns.Family, "hr.imports.jobs.family", HintKey: "hr.imports.jobs.family_hint", MaxLength: Job.FamilyMaxLength),
        new(Columns.Level, "hr.imports.jobs.level", HintKey: "hr.imports.jobs.level_hint", MaxLength: Job.LevelMaxLength),
    ];

    public async Task<ImportValidation> ValidateAsync(IReadOnlyList<ImportRow> rows, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new ImportErrorList(localizer, context.Culture);
        var granted = await HrImports.ScopesAsync(scopes, currentUser, context, Permission, cancellationToken);
        if (!granted.Any(s => s.Type == AccessScopeTypes.Tenant))
        {
            errors.AddForFile(OrgErrors.JobsNeedTenantScope.Code);
            return new ImportValidation(errors.Items);
        }

        var existing = await JobImportLookup.ExistingCodesAsync(db, rows, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var code = Code.Create(row.Text(Columns.Code), Job.CodeMaxLength);
            if (code.IsFailure)
            {
                errors.Add(row, Columns.Code, code.Error.Code, code.Error.Args);
            }
            else if (!seen.Add(code.Value.Value))
            {
                errors.Add(row, Columns.Code, HrImportErrors.DuplicateInFileCode);
            }
            else if (existing.Contains(code.Value.Value))
            {
                errors.Add(row, Columns.Code, CommonErrors.Duplicate.Code);
            }
        }

        return new ImportValidation(errors.Items);
    }

    public IImportBatchCommand CreateBatch(IReadOnlyList<ImportRow> rows, ImportContext context) => new ImportJobsCommand(rows);
}

/// <summary>Writes a batch of validated job rows in one transaction (D35).</summary>
public sealed record ImportJobsCommand(IReadOnlyList<ImportRow> Rows) : IImportBatchCommand;

internal static class JobImportLookup
{
    public static async Task<HashSet<string>> ExistingCodesAsync(IHrDbContext db, IEnumerable<ImportRow> rows, CancellationToken cancellationToken)
    {
        var codes = rows.Select(r => HrImports.Code(r.Text(JobImport.Columns.Code))).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return [.. await db.Jobs.AsNoTracking().Where(j => codes.Contains(j.Code)).Select(j => j.Code).ToListAsync(cancellationToken)];
    }
}

internal sealed class ImportJobsCommandHandler(IHrDbContext db) : ICommandHandler<ImportJobsCommand>
{
    public async Task<Result> Handle(ImportJobsCommand request, CancellationToken cancellationToken)
    {
        var existing = await JobImportLookup.ExistingCodesAsync(db, request.Rows, cancellationToken);
        var errors = new List<FieldError>();
        foreach (var row in request.Rows)
        {
            // The rows were validated; what changed since (a job added by hand) still fails the batch
            var code = Code.Create(row.Text(JobImport.Columns.Code), Job.CodeMaxLength);
            if (code.IsFailure || !existing.Add(code.Value.Value))
            {
                errors.Add(HrImports.BatchError(row, JobImport.Columns.Code, CommonErrors.Duplicate.Code));
                continue;
            }

            db.Jobs.Add(Job.Create(code.Value, new JobProfile(
                row.Text(JobImport.Columns.Name)!.Trim(),
                row.Text(JobImport.Columns.Family)?.Trim(),
                row.Text(JobImport.Columns.Level)?.Trim())));
        }

        return errors.Count > 0 ? Error.Validation(errors) : Result.Success();
    }
}
