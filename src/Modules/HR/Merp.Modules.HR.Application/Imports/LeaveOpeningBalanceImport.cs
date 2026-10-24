using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Imports;

/// <summary>
/// Opening leave balances from the previous system (PLAN L3, D43): the person by TCKN, their open employment in the
/// row's company, the balance (YILLIK unless given), the day and the days. One opening balance per balance; accruals
/// before the accrual start setting are not written, so the opening balance carries that history.
/// </summary>
public sealed class LeaveOpeningBalanceImport(
    IHrDbContext db,
    IOrganizationLookup organization,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    ITextLocalizer localizer) : IImportDefinition
{
    public const string ImportKey = "hr.leave-opening-balances";

    public static class Columns
    {
        public const string Company = "companyCode";
        public const string Tckn = "tckn";
        public const string LeaveType = "leaveTypeCode";
        public const string Date = "date";
        public const string Days = "days";
        public const string Note = "note";
    }

    public string Key => ImportKey;

    public string NameKey => "hr.imports.leave_opening_balances.name";

    public string Permission => HrPermissions.Leave.Balances.Manage;

    IReadOnlyList<ImportColumn> IImportDefinition.Columns { get; } =
    [
        new(Columns.Company, "hr.imports.company", Required: true, HintKey: "hr.imports.company_hint"),
        new(Columns.Tckn, "hr.imports.employees.tckn", Required: true),
        new(Columns.LeaveType, "hr.imports.leave_opening_balances.leave_type", HintKey: "hr.imports.leave_opening_balances.leave_type_hint"),
        new(Columns.Date, "hr.imports.leave_opening_balances.date", ImportColumnType.Date, Required: true, HintKey: "hr.imports.leave_opening_balances.date_hint"),
        new(Columns.Days, "hr.imports.leave_opening_balances.days", ImportColumnType.Number, Required: true, HintKey: "hr.imports.leave_opening_balances.days_hint"),
        new(Columns.Note, "hr.imports.leave_opening_balances.note", MaxLength: LeaveLedgerEntry.NoteMaxLength),
    ];

    public async Task<ImportValidation> ValidateAsync(IReadOnlyList<ImportRow> rows, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new ImportErrorList(localizer, context.Culture);
        var scope = await HrImports.CompanyScopeAsync(scopes, currentUser, context, Permission, cancellationToken);
        var lookup = await OpeningBalanceLookup.LoadAsync(db, organization, rows, cancellationToken);
        var seen = new HashSet<(string, Guid, string)>();
        foreach (var row in rows)
        {
            var company = errors.Company(row, Columns.Company, lookup.Companies, scope);
            var tckn = Tckn.Create(row.Text(Columns.Tckn));
            if (tckn.IsFailure)
            {
                errors.Add(row, Columns.Tckn, tckn.Error.Code, tckn.Error.Args);
            }

            if (row.AsDecimal(Columns.Days) is { } days && (days == 0 || Math.Abs(days) > 999))
            {
                errors.Add(row, Columns.Days, "hr.leave.errors.invalid_quantity");
            }

            if (company is null || tckn.IsFailure)
            {
                continue;
            }

            var employment = lookup.Employment(tckn.Value.Value, company.Id);
            if (employment is null)
            {
                errors.Add(row, Columns.Tckn, HrImportErrors.NoOpenEmploymentCode);
                continue;
            }

            var code = lookup.TypeCode(company.Id, row.Text(Columns.LeaveType));
            if (code is null)
            {
                errors.Add(row, Columns.LeaveType, LeaveBalanceErrors.NoBalanceTypeCode);
            }
            else if (!seen.Add((tckn.Value.Value, company.Id, code)))
            {
                errors.Add(row, Columns.Tckn, HrImportErrors.DuplicateInFileCode);
            }
            else if (lookup.HasOpening(employment.Value, code))
            {
                errors.Add(row, Columns.Days, "hr.leave.errors.opening_balance_exists");
            }
        }

        return new ImportValidation(errors.Items);
    }

    public IImportBatchCommand CreateBatch(IReadOnlyList<ImportRow> rows, ImportContext context) => new ImportLeaveOpeningBalancesCommand(rows);
}

/// <summary>Writes a batch of validated opening balances in one transaction (D35).</summary>
public sealed record ImportLeaveOpeningBalancesCommand(IReadOnlyList<ImportRow> Rows) : IImportBatchCommand;

/// <summary>Companies, open employments by TCKN, balance types and existing opening balances, loaded once.</summary>
internal sealed class OpeningBalanceLookup
{
    private Dictionary<(string, Guid), EmploymentId> _employments = [];
    private Dictionary<Guid, List<LeaveType>> _types = [];
    private HashSet<(EmploymentId, string)> _openings = [];

    public Dictionary<string, CompanySummary> Companies { get; private set; } = [];

    public static async Task<OpeningBalanceLookup> LoadAsync(IHrDbContext db, IOrganizationLookup organization, IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        var companies = await HrImports.CompaniesAsync(organization, rows, LeaveOpeningBalanceImport.Columns.Company, cancellationToken);
        var tcknList = rows.Select(r => r.Text(LeaveOpeningBalanceImport.Columns.Tckn)?.Trim()).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var employments = await db.Employees.AsNoTracking()
            .Where(e => tcknList.Contains(e.Tckn))
            .SelectMany(e => e.Employments.Where(m => m.TerminationDate == null).Select(m => new { e.Tckn, m.CompanyId, m.Id }))
            .ToListAsync(cancellationToken);
        var employmentIds = employments.Select(m => m.Id).ToList();
        var openings = await db.LeaveAccounts.AsNoTracking()
            .Where(a => employmentIds.Contains(a.EmploymentId) && a.Entries.Any(e => e.Source == LeaveLedgerSource.OpeningBalance))
            .Select(a => new { a.EmploymentId, a.LeaveTypeCode })
            .ToListAsync(cancellationToken);
        var types = new Dictionary<Guid, List<LeaveType>>();
        foreach (var company in companies.Values)
        {
            types[company.Id] = await LeaveBalanceTypes.ForCompanyAsync(db, company.Id, cancellationToken);
        }

        return new OpeningBalanceLookup
        {
            Companies = companies,
            _employments = employments.ToDictionary(m => (m.Tckn, m.CompanyId), m => m.Id),
            _types = types,
            _openings = [.. openings.Select(o => (o.EmploymentId, o.LeaveTypeCode))],
        };
    }

    public EmploymentId? Employment(string tckn, Guid companyId) => _employments.TryGetValue((tckn, companyId), out var id) ? id : null;

    /// <summary>The balance a row names, or the company's annual one when it names none.</summary>
    public string? TypeCode(Guid companyId, string? code)
    {
        var types = _types.GetValueOrDefault(companyId) ?? [];
        return string.IsNullOrWhiteSpace(code)
            ? types.Where(t => t.Category == LeaveCategory.Annual).OrderBy(t => t.CompanyId is null).FirstOrDefault()?.Code
            : types.FirstOrDefault(t => string.Equals(t.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))?.Code;
    }

    public bool HasOpening(EmploymentId employmentId, string code) => _openings.Contains((employmentId, code));
}

internal sealed class ImportLeaveOpeningBalancesCommandHandler(IHrDbContext db, IOrganizationLookup organization, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<ImportLeaveOpeningBalancesCommand>
{
    public async Task<Result> Handle(ImportLeaveOpeningBalancesCommand request, CancellationToken cancellationToken)
    {
        var lookup = await OpeningBalanceLookup.LoadAsync(db, organization, request.Rows, cancellationToken);
        var stamp = LeaveBalanceTypes.Stamp(timeProvider, currentUser);
        var errors = new List<FieldError>();
        foreach (var row in request.Rows)
        {
            // The rows were validated; what changed since still fails the batch
            var company = HrImports.Code(row.Text(LeaveOpeningBalanceImport.Columns.Company)) is { } companyCode ? lookup.Companies.GetValueOrDefault(companyCode) : null;
            var tckn = row.Text(LeaveOpeningBalanceImport.Columns.Tckn)?.Trim();
            var employmentId = company is null || tckn is null ? null : lookup.Employment(tckn, company.Id);
            var code = company is null ? null : lookup.TypeCode(company.Id, row.Text(LeaveOpeningBalanceImport.Columns.LeaveType));
            var employee = employmentId is null ? null : await db.Employees.Include(e => e.Employments)
                .SingleOrDefaultAsync(e => e.Employments.Any(m => m.Id == employmentId), cancellationToken);
            var employment = employee?.Employments.Single(m => m.Id == employmentId);
            if (employee is null || employment is null || code is null)
            {
                errors.Add(HrImports.BatchError(row, null, CommonErrors.ValidationCode));
                continue;
            }

            var account = await LeaveBalanceTypes.AccountAsync(db, employee, employment, code, cancellationToken);
            var opened = account.OpeningBalance(
                row.AsDate(LeaveOpeningBalanceImport.Columns.Date)!.Value,
                row.AsDecimal(LeaveOpeningBalanceImport.Columns.Days)!.Value,
                row.Text(LeaveOpeningBalanceImport.Columns.Note),
                stamp);
            if (opened.IsFailure)
            {
                errors.AddRange(opened.Error.Fields.Select(f => HrImports.BatchError(row, LeaveOpeningBalanceImport.Columns.Days, f.Code)));
            }
        }

        return errors.Count > 0 ? Error.Validation(errors) : Result.Success();
    }
}
