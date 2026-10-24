using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Imports;

/// <summary>
/// Positions from Excel (PLAN H4, D40): the unit and the job by their codes, the position reported to among the
/// company's open positions or the rows above. A position may be marked as its unit's manager. Needs
/// <c>hr.org.manage</c> with a scope covering each row's company.
/// </summary>
public sealed class PositionImport(
    IHrDbContext db,
    IOrganizationLookup organization,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    ITextLocalizer localizer) : IImportDefinition
{
    public const string ImportKey = "hr.positions";

    public static class Columns
    {
        public const string Company = "companyCode";
        public const string Code = "code";
        public const string OrgUnit = "orgUnitCode";
        public const string Job = "jobCode";
        public const string ReportsTo = "reportsToCode";
        public const string Norm = "norm";
        public const string IsManager = "isManager";
        public const string ManagesUnit = "managesUnit";
    }

    public string Key => ImportKey;

    public string NameKey => "hr.imports.positions.name";

    public string Permission => HrPermissions.Org.Manage;

    IReadOnlyList<ImportColumn> IImportDefinition.Columns { get; } =
    [
        new(Columns.Company, "hr.imports.company", Required: true, HintKey: "hr.imports.company_hint"),
        new(Columns.Code, "hr.imports.positions.code", Required: true, HintKey: "hr.imports.code_hint", MaxLength: Position.CodeMaxLength),
        new(Columns.OrgUnit, "hr.imports.positions.org_unit", Required: true, HintKey: "hr.imports.positions.org_unit_hint"),
        new(Columns.Job, "hr.imports.positions.job", Required: true, HintKey: "hr.imports.positions.job_hint"),
        new(Columns.ReportsTo, "hr.imports.positions.reports_to", HintKey: "hr.imports.positions.reports_to_hint"),
        new(Columns.Norm, "hr.imports.positions.norm", ImportColumnType.WholeNumber, HintKey: "hr.imports.positions.norm_hint"),
        new(Columns.IsManager, "hr.imports.positions.is_manager", ImportColumnType.Boolean),
        new(Columns.ManagesUnit, "hr.imports.positions.manages_unit", ImportColumnType.Boolean, HintKey: "hr.imports.positions.manages_unit_hint"),
    ];

    public async Task<ImportValidation> ValidateAsync(IReadOnlyList<ImportRow> rows, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new ImportErrorList(localizer, context.Culture);
        var scope = await HrImports.CompanyScopeAsync(scopes, currentUser, context, Permission, cancellationToken);
        var lookup = await PositionImportLookup.LoadAsync(db, organization, rows, cancellationToken);

        var above = new HashSet<(Guid, string)>();
        var managedAbove = new HashSet<OrgUnitId>();
        var all = rows.Select(r => (HrImports.Code(r.Text(Columns.Company)), HrImports.Code(r.Text(Columns.Code)))).ToHashSet();
        foreach (var row in rows)
        {
            var company = errors.Company(row, Columns.Company, lookup.Companies, scope);
            var code = Code.Create(row.Text(Columns.Code), Position.CodeMaxLength);
            if (code.IsFailure)
            {
                errors.Add(row, Columns.Code, code.Error.Code, code.Error.Args);
            }

            var job = HrImports.Code(row.Text(Columns.Job)) is { } jobCode ? lookup.Job(jobCode) : null;
            if (job is null)
            {
                errors.Add(row, Columns.Job, OrgErrors.UnknownJobCode);
            }
            else if (!job.IsActive)
            {
                errors.Add(row, Columns.Job, OrgErrors.JobInactiveCode);
            }

            if (row.AsDecimal(Columns.Norm) is { } norm && norm is < 1 or > Position.MaxNorm)
            {
                errors.Add(row, Columns.Norm, "common.errors.between", new Dictionary<string, object?> { ["from"] = 1, ["to"] = Position.MaxNorm });
            }

            if (company is null)
            {
                continue;
            }

            if (code.IsSuccess)
            {
                if (above.Contains((company.Id, code.Value.Value)))
                {
                    errors.Add(row, Columns.Code, HrImportErrors.DuplicateInFileCode);
                }
                else if (lookup.Position(company.Id, code.Value.Value) is not null)
                {
                    errors.Add(row, Columns.Code, CommonErrors.Duplicate.Code);
                }
            }

            var unit = HrImports.Code(row.Text(Columns.OrgUnit)) is { } unitCode ? lookup.Unit(company.Id, unitCode) : null;
            if (unit is null)
            {
                errors.Add(row, Columns.OrgUnit, OrgErrors.UnknownOrgUnitCode);
            }
            else if (row.AsBoolean(Columns.ManagesUnit) == true && (unit.ManagerPositionId is not null || !managedAbove.Add(unit.Id)))
            {
                errors.Add(row, Columns.ManagesUnit, HrImportErrors.UnitHasManagerCode);
            }

            if (HrImports.Code(row.Text(Columns.ReportsTo)) is { } reportsTo
                && !(lookup.Position(company.Id, reportsTo) is { Status: not PositionStatus.Closed })
                && !above.Contains((company.Id, reportsTo)))
            {
                errors.Add(row, Columns.ReportsTo, all.Contains((company.Code, reportsTo)) ? HrImportErrors.LaterInFileCode : OrgErrors.InvalidReportsToCode);
            }

            if (code.IsSuccess)
            {
                above.Add((company.Id, code.Value.Value));
            }
        }

        return new ImportValidation(errors.Items);
    }

    public IImportBatchCommand CreateBatch(IReadOnlyList<ImportRow> rows, ImportContext context) => new ImportPositionsCommand(rows);
}

/// <summary>Writes a batch of validated position rows in one transaction (D35).</summary>
public sealed record ImportPositionsCommand(IReadOnlyList<ImportRow> Rows) : IImportBatchCommand;

/// <summary>Companies, their units and positions, and the jobs the rows refer to, loaded once.</summary>
internal sealed class PositionImportLookup
{
    private Dictionary<(Guid, string), OrgUnit> _units = [];
    private Dictionary<(Guid, string), Position> _positions = [];
    private Dictionary<string, Job> _jobs = [];

    public Dictionary<string, CompanySummary> Companies { get; private set; } = [];

    /// <summary>The lookup of <paramref name="rows"/>; with <paramref name="track"/> the units are tracked, so that a batch can name their managers.</summary>
    public static async Task<PositionImportLookup> LoadAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        IReadOnlyList<ImportRow> rows,
        CancellationToken cancellationToken,
        bool track = false)
    {
        var companies = await HrImports.CompaniesAsync(organization, rows, PositionImport.Columns.Company, cancellationToken);
        var companyIds = companies.Values.Select(c => c.Id).ToList();
        var jobCodes = rows.Select(r => HrImports.Code(r.Text(PositionImport.Columns.Job))).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var units = track ? db.OrgUnits : db.OrgUnits.AsNoTracking();
        return new PositionImportLookup
        {
            Companies = companies,
            _units = (await units.Where(u => companyIds.Contains(u.CompanyId)).ToListAsync(cancellationToken)).ToDictionary(u => (u.CompanyId, u.Code)),
            _positions = (await db.Positions.AsNoTracking().Where(p => companyIds.Contains(p.CompanyId)).ToListAsync(cancellationToken))
                .ToDictionary(p => (p.CompanyId, p.Code)),
            _jobs = (await db.Jobs.AsNoTracking().Where(j => jobCodes.Contains(j.Code)).ToListAsync(cancellationToken)).ToDictionary(j => j.Code, StringComparer.Ordinal),
        };
    }

    public OrgUnit? Unit(Guid companyId, string code) => _units.GetValueOrDefault((companyId, code));

    public Position? Position(Guid companyId, string code) => _positions.GetValueOrDefault((companyId, code));

    public Job? Job(string code) => _jobs.GetValueOrDefault(code);

    /// <summary>A position added by the batch, so that the rows below may report to it.</summary>
    public void Add(Position position) => _positions[(position.CompanyId, position.Code)] = position;
}

internal sealed class ImportPositionsCommandHandler(IHrDbContext db, IOrganizationLookup organization) : ICommandHandler<ImportPositionsCommand>
{
    public async Task<Result> Handle(ImportPositionsCommand request, CancellationToken cancellationToken)
    {
        var lookup = await PositionImportLookup.LoadAsync(db, organization, request.Rows, cancellationToken, track: true);
        var errors = new List<FieldError>();
        foreach (var row in request.Rows)
        {
            // The rows were validated; what changed since still fails the batch
            var company = HrImports.Code(row.Text(PositionImport.Columns.Company)) is { } companyCode ? lookup.Companies.GetValueOrDefault(companyCode) : null;
            var code = Code.Create(row.Text(PositionImport.Columns.Code), Position.CodeMaxLength);
            var unit = company is not null && HrImports.Code(row.Text(PositionImport.Columns.OrgUnit)) is { } unitCode ? lookup.Unit(company.Id, unitCode) : null;
            var job = HrImports.Code(row.Text(PositionImport.Columns.Job)) is { } jobCode ? lookup.Job(jobCode) : null;
            if (company is null || code.IsFailure || unit is null || job is not { IsActive: true })
            {
                errors.Add(HrImports.BatchError(row, null, CommonErrors.ValidationCode));
                continue;
            }

            if (lookup.Position(company.Id, code.Value.Value) is not null)
            {
                errors.Add(HrImports.BatchError(row, PositionImport.Columns.Code, CommonErrors.Duplicate.Code));
                continue;
            }

            PositionId? reportsTo = null;
            if (HrImports.Code(row.Text(PositionImport.Columns.ReportsTo)) is { } reportsToCode)
            {
                if (lookup.Position(company.Id, reportsToCode) is not { Status: not PositionStatus.Closed } manager)
                {
                    errors.Add(HrImports.BatchError(row, PositionImport.Columns.ReportsTo, OrgErrors.InvalidReportsToCode));
                    continue;
                }

                reportsTo = manager.Id;
            }

            var managesUnit = row.AsBoolean(PositionImport.Columns.ManagesUnit) == true;
            if (managesUnit && unit.ManagerPositionId is not null)
            {
                errors.Add(HrImports.BatchError(row, PositionImport.Columns.ManagesUnit, HrImportErrors.UnitHasManagerCode));
                continue;
            }

            var position = Position.Create(company.Id, code.Value, new PositionProfile(
                unit.Id,
                job.Id,
                reportsTo,
                (int?)row.AsDecimal(PositionImport.Columns.Norm) ?? 1,
                PositionStatus.Active,
                row.AsBoolean(PositionImport.Columns.IsManager) == true));
            db.Positions.Add(position);
            lookup.Add(position);
            if (managesUnit)
            {
                unit.ChangeManager(position.Id);
            }
        }

        return errors.Count > 0 ? Error.Validation(errors) : Result.Success();
    }
}
