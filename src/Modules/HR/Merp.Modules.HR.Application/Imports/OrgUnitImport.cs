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
/// Units of companies from Excel (PLAN H4, D40): the company by its code, the parent unit by its code, among the
/// company's units or the rows above. Needs <c>hr.org.manage</c> with a scope covering each row's company.
/// </summary>
public sealed class OrgUnitImport(
    IHrDbContext db,
    IOrganizationLookup organization,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    ITextLocalizer localizer) : IImportDefinition
{
    public const string ImportKey = "hr.org-units";

    public static class Columns
    {
        public const string Company = "companyCode";
        public const string Code = "code";
        public const string Name = "name";
        public const string Type = "type";
        public const string Parent = "parentCode";
        public const string CostCenter = "costCenterCode";
        public const string Branch = "branchCode";
        public const string ValidFrom = "validFrom";
    }

    public string Key => ImportKey;

    public string NameKey => "hr.imports.org_units.name";

    public string Permission => HrPermissions.Org.Manage;

    IReadOnlyList<ImportColumn> IImportDefinition.Columns { get; } =
    [
        new(Columns.Company, "hr.imports.company", Required: true, HintKey: "hr.imports.company_hint"),
        new(Columns.Code, "hr.imports.org_units.code", Required: true, HintKey: "hr.imports.code_hint", MaxLength: OrgUnit.CodeMaxLength),
        new(Columns.Name, "hr.imports.org_units.unit_name", Required: true, MaxLength: OrgUnit.NameMaxLength),
        new(Columns.Type, "hr.imports.org_units.type", ImportColumnType.Choice, Required: true,
            Choices: [.. Enum.GetValues<OrgUnitType>().Select(t => new ImportChoice(t.ToString(), "web.hr.org_unit_type." + t))]),
        new(Columns.Parent, "hr.imports.org_units.parent", HintKey: "hr.imports.org_units.parent_hint"),
        new(Columns.CostCenter, "hr.imports.org_units.cost_center", MaxLength: OrgUnit.CostCenterCodeMaxLength),
        new(Columns.Branch, "hr.imports.branch", HintKey: "hr.imports.branch_hint"),
        new(Columns.ValidFrom, "hr.imports.org_units.valid_from", ImportColumnType.Date),
    ];

    public async Task<ImportValidation> ValidateAsync(IReadOnlyList<ImportRow> rows, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new ImportErrorList(localizer, context.Culture);
        var scope = await HrImports.CompanyScopeAsync(scopes, currentUser, context, Permission, cancellationToken);
        var lookup = await OrgUnitImportLookup.LoadAsync(db, organization, rows, cancellationToken);

        // Units of the rows above, by company: a parent may come from the file if it comes first
        var above = new HashSet<(Guid, string)>();
        var all = rows.Select(r => (HrImports.Code(r.Text(Columns.Company)), HrImports.Code(r.Text(Columns.Code)))).ToHashSet();
        foreach (var row in rows)
        {
            var company = errors.Company(row, Columns.Company, lookup.Companies, scope);
            var code = Code.Create(row.Text(Columns.Code), OrgUnit.CodeMaxLength);
            if (code.IsFailure)
            {
                errors.Add(row, Columns.Code, code.Error.Code, code.Error.Args);
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
                else if (lookup.Unit(company.Id, code.Value.Value) is not null)
                {
                    errors.Add(row, Columns.Code, CommonErrors.Duplicate.Code);
                }
            }

            if (HrImports.Code(row.Text(Columns.Parent)) is { } parent
                && lookup.Unit(company.Id, parent) is null
                && !above.Contains((company.Id, parent)))
            {
                errors.Add(row, Columns.Parent, all.Contains((company.Code, parent)) ? HrImportErrors.LaterInFileCode : OrgErrors.UnknownParentCode);
            }

            if (HrImports.Code(row.Text(Columns.Branch)) is { } branch && lookup.Branch(company.Id, branch) is null)
            {
                errors.Add(row, Columns.Branch, OrgErrors.UnknownBranchCode);
            }

            if (code.IsSuccess)
            {
                above.Add((company.Id, code.Value.Value));
            }
        }

        return new ImportValidation(errors.Items);
    }

    public IImportBatchCommand CreateBatch(IReadOnlyList<ImportRow> rows, ImportContext context) => new ImportOrgUnitsCommand(rows);
}

/// <summary>Writes a batch of validated unit rows in one transaction (D35).</summary>
public sealed record ImportOrgUnitsCommand(IReadOnlyList<ImportRow> Rows) : IImportBatchCommand;

/// <summary>Companies, their units and active branches the rows refer to, loaded once.</summary>
internal sealed class OrgUnitImportLookup
{
    private Dictionary<(Guid, string), OrgUnit> _units = [];
    private Dictionary<(Guid, string), Guid> _branches = [];

    public Dictionary<string, CompanySummary> Companies { get; private set; } = [];

    public static async Task<OrgUnitImportLookup> LoadAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        IReadOnlyList<ImportRow> rows,
        CancellationToken cancellationToken)
    {
        var companies = await HrImports.CompaniesAsync(organization, rows, OrgUnitImport.Columns.Company, cancellationToken);
        var companyIds = companies.Values.Select(c => c.Id).ToList();
        var units = await db.OrgUnits.AsNoTracking().Where(u => companyIds.Contains(u.CompanyId)).ToListAsync(cancellationToken);
        var branches = await organization.GetBranchesOfCompaniesAsync(companyIds, cancellationToken);
        return new OrgUnitImportLookup
        {
            Companies = companies,
            _units = units.ToDictionary(u => (u.CompanyId, u.Code)),
            _branches = branches.Where(b => b.IsActive).ToDictionary(b => (b.CompanyId, b.Code), b => b.Id),
        };
    }

    public OrgUnit? Unit(Guid companyId, string code) => _units.GetValueOrDefault((companyId, code));

    public Guid? Branch(Guid companyId, string code) => _branches.TryGetValue((companyId, code), out var id) ? id : null;

    /// <summary>A unit added by the batch, so that the rows below find it as their parent.</summary>
    public void Add(OrgUnit unit) => _units[(unit.CompanyId, unit.Code)] = unit;
}

internal sealed class ImportOrgUnitsCommandHandler(IHrDbContext db, IOrganizationLookup organization) : ICommandHandler<ImportOrgUnitsCommand>
{
    public async Task<Result> Handle(ImportOrgUnitsCommand request, CancellationToken cancellationToken)
    {
        var lookup = await OrgUnitImportLookup.LoadAsync(db, organization, request.Rows, cancellationToken);
        var errors = new List<FieldError>();
        foreach (var row in request.Rows)
        {
            // The rows were validated; what changed since (a unit added by hand) still fails the batch
            var company = HrImports.Code(row.Text(OrgUnitImport.Columns.Company)) is { } companyCode ? lookup.Companies.GetValueOrDefault(companyCode) : null;
            var code = Code.Create(row.Text(OrgUnitImport.Columns.Code), OrgUnit.CodeMaxLength);
            if (company is null || code.IsFailure)
            {
                errors.Add(HrImports.BatchError(row, null, CommonErrors.ValidationCode));
                continue;
            }

            if (lookup.Unit(company.Id, code.Value.Value) is not null)
            {
                errors.Add(HrImports.BatchError(row, OrgUnitImport.Columns.Code, CommonErrors.Duplicate.Code));
                continue;
            }

            OrgUnit? parent = null;
            if (HrImports.Code(row.Text(OrgUnitImport.Columns.Parent)) is { } parentCode && (parent = lookup.Unit(company.Id, parentCode)) is null)
            {
                errors.Add(HrImports.BatchError(row, OrgUnitImport.Columns.Parent, OrgErrors.UnknownParentCode));
                continue;
            }

            Guid? branchId = null;
            if (HrImports.Code(row.Text(OrgUnitImport.Columns.Branch)) is { } branchCode && (branchId = lookup.Branch(company.Id, branchCode)) is null)
            {
                errors.Add(HrImports.BatchError(row, OrgUnitImport.Columns.Branch, OrgErrors.UnknownBranchCode));
                continue;
            }

            var unit = OrgUnit.Create(company.Id, parent, code.Value, new OrgUnitProfile(
                row.Text(OrgUnitImport.Columns.Name)!.Trim(),
                Enum.Parse<OrgUnitType>(row.Text(OrgUnitImport.Columns.Type)!),
                row.Text(OrgUnitImport.Columns.CostCenter)?.Trim(),
                branchId,
                row.AsDate(OrgUnitImport.Columns.ValidFrom),
                null));
            db.OrgUnits.Add(unit);
            lookup.Add(unit);
        }

        return errors.Count > 0 ? Error.Validation(errors) : Result.Success();
    }
}
