using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Imports;

/// <summary>
/// Employees from Excel (PLAN H4, D40): the person, the employment and the first assignment on one row, the company
/// and the position by their codes. A new TCKN is hired with <c>hr.employees.create</c>. In
/// <see cref="ImportMode.CreateOrUpdate"/> a known TCKN updates the person and the primary e-mail and phone, with
/// <c>hr.employees.update</c> and an open employment in the row's company; its employment columns are not used, job
/// changes are made on the card with their date and reason.
/// </summary>
public sealed class EmployeeImport(
    IHrDbContext db,
    IOrganizationLookup organization,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    ICustomFieldValidator customFields,
    ITextLocalizer localizer) : IImportDefinition
{
    public const string ImportKey = "hr.employees";

    public static class Columns
    {
        public const string Company = "companyCode";
        public const string Tckn = "tckn";
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string BirthDate = "birthDate";
        public const string BirthPlace = "birthPlace";
        public const string Gender = "gender";
        public const string Nationality = "nationality";
        public const string MaritalStatus = "maritalStatus";
        public const string MilitaryStatus = "militaryStatus";
        public const string Email = "email";
        public const string Phone = "phone";
        public const string HireDate = "hireDate";
        public const string EmploymentType = "employmentType";
        public const string ContractEndDate = "contractEndDate";
        public const string WeeklyHours = "weeklyHours";
        public const string ProbationEndDate = "probationEndDate";
        public const string SeniorityDate = "seniorityDate";
        public const string SeniorityReason = "seniorityReason";
        public const string Position = "positionCode";
        public const string Branch = "branchCode";
        public const string ManagerPosition = "managerPositionCode";
        public const string Level = "level";
        public const string CostCenter = "costCenterCode";
    }

    public string Key => ImportKey;

    public string NameKey => "hr.imports.employees.name";

    public string Permission => HrPermissions.Employees.Create;

    public bool SupportsUpdate => true;

    IReadOnlyList<ImportColumn> IImportDefinition.Columns { get; } =
    [
        new(Columns.Company, "hr.imports.company", Required: true, HintKey: "hr.imports.company_hint"),
        new(Columns.Tckn, "hr.imports.employees.tckn", Required: true, HintKey: "hr.imports.employees.tckn_hint"),
        new(Columns.FirstName, "hr.imports.employees.first_name", Required: true, MaxLength: Employee.NameMaxLength),
        new(Columns.LastName, "hr.imports.employees.last_name", Required: true, MaxLength: Employee.NameMaxLength),
        new(Columns.BirthDate, "hr.imports.employees.birth_date", ImportColumnType.Date),
        new(Columns.BirthPlace, "hr.imports.employees.birth_place", MaxLength: Employee.PlaceMaxLength),
        Choice<Gender>(Columns.Gender, "hr.imports.employees.gender", "web.hr.gender."),
        new(Columns.Nationality, "hr.imports.employees.nationality", HintKey: "hr.imports.employees.nationality_hint", MaxLength: 2),
        Choice<MaritalStatus>(Columns.MaritalStatus, "hr.imports.employees.marital_status", "web.hr.marital_status."),
        Choice<MilitaryStatus>(Columns.MilitaryStatus, "hr.imports.employees.military_status", "web.hr.military_status."),
        new(Columns.Email, "hr.imports.employees.email", HintKey: "hr.imports.employees.email_hint", MaxLength: 200),
        new(Columns.Phone, "hr.imports.employees.phone", HintKey: "hr.imports.employees.phone_hint", MaxLength: 30),
        new(Columns.HireDate, "hr.imports.employees.hire_date", ImportColumnType.Date, HintKey: "hr.imports.employees.new_only_hint"),
        Choice<EmploymentType>(Columns.EmploymentType, "hr.imports.employees.employment_type", "web.hr.employment_type.", "hr.imports.employees.new_only_hint"),
        new(Columns.ContractEndDate, "hr.imports.employees.contract_end_date", ImportColumnType.Date, HintKey: "hr.imports.employees.contract_end_date_hint"),
        new(Columns.WeeklyHours, "hr.imports.employees.weekly_hours", ImportColumnType.Number, HintKey: "hr.imports.employees.weekly_hours_hint"),
        new(Columns.ProbationEndDate, "hr.imports.employees.probation_end_date", ImportColumnType.Date),
        new(Columns.SeniorityDate, "hr.imports.employees.seniority_date", ImportColumnType.Date, HintKey: "hr.imports.employees.seniority_date_hint"),
        new(Columns.SeniorityReason, "hr.imports.employees.seniority_reason", MaxLength: Employment.TextMaxLength),
        new(Columns.Position, "hr.imports.employees.position", HintKey: "hr.imports.employees.position_hint"),
        new(Columns.Branch, "hr.imports.branch", HintKey: "hr.imports.branch_hint"),
        new(Columns.ManagerPosition, "hr.imports.employees.manager_position", HintKey: "hr.imports.employees.manager_position_hint"),
        new(Columns.Level, "hr.imports.employees.level", MaxLength: JobAssignment.LevelMaxLength),
        new(Columns.CostCenter, "hr.imports.employees.cost_center", MaxLength: JobAssignment.CostCenterCodeMaxLength),
    ];

    public async Task<ImportValidation> ValidateAsync(IReadOnlyList<ImportRow> rows, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new ImportErrorList(localizer, context.Culture);
        var updating = context.Mode == ImportMode.CreateOrUpdate;
        var createScope = await HrImports.CompanyScopeAsync(scopes, currentUser, context, Permission, cancellationToken);
        var updateScope = updating
            ? await HrImports.CompanyScopeAsync(scopes, currentUser, context, HrPermissions.Employees.Update, cancellationToken)
            : createScope;
        var lookup = await EmployeeImportLookup.LoadAsync(db, organization, rows, cancellationToken);
        var fieldRules = new CustomFieldDefaults(customFields);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var updated = new List<int>();
        foreach (var row in rows)
        {
            var tckn = Tckn.Create(row.Text(Columns.Tckn));
            if (tckn.IsFailure)
            {
                errors.Add(row, Columns.Tckn, tckn.Error.Code, tckn.Error.Args);
            }
            else if (!seen.Add(tckn.Value.Value))
            {
                errors.Add(row, Columns.Tckn, HrImportErrors.DuplicateInFileCode);
            }

            EmployeeImportRows.CheckPerson(row, errors);
            var existing = tckn.IsSuccess ? lookup.Employee(tckn.Value.Value) : null;
            if (existing is not null && !updating)
            {
                errors.Add(row, Columns.Tckn, EmployeeCommandErrors.TcknExistsCode);
                continue;
            }

            var company = errors.Company(row, Columns.Company, lookup.Companies, existing is null ? createScope : updateScope);
            if (existing is not null)
            {
                if (company is not null && !existing.OpenCompanyIds.Contains(company.Id))
                {
                    errors.Add(row, Columns.Company, HrImportErrors.NoOpenEmploymentCode);
                }

                if (!errors.HasErrors(row))
                {
                    updated.Add(row.Number);
                }

                continue;
            }

            if (company is null)
            {
                continue;
            }

            if (EmployeeImportRows.Hire(row, company.Id, lookup) is { IsFailure: true } hire)
            {
                // An invalid TCKN has its error already
                foreach (var field in hire.Error.Fields.Where(f => f.Field != "tckn"))
                {
                    errors.Add(row, EmployeeImportRows.Column(field.Field), field.Code, field.Args);
                }
            }

            if (await fieldRules.ValidateAsync(company.Id, cancellationToken) is { IsFailure: true } values)
            {
                foreach (var field in values.Error.Fields)
                {
                    errors.Add(row, null, HrImportErrors.CustomFieldCode, new Dictionary<string, object?>
                    {
                        ["field"] = field.Field.Replace("customFields.", string.Empty, StringComparison.Ordinal),
                        ["message"] = localizer.Get(field.Code, field.Args, context.Culture),
                    });
                }
            }
        }

        return new ImportValidation(errors.Items, updated);
    }

    public IImportBatchCommand CreateBatch(IReadOnlyList<ImportRow> rows, ImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ImportEmployeesCommand(rows, context.Mode == ImportMode.CreateOrUpdate);
    }

    private static ImportColumn Choice<TEnum>(string key, string headerKey, string labelPrefix, string? hintKey = null)
        where TEnum : struct, Enum =>
        new(key, headerKey, ImportColumnType.Choice, HintKey: hintKey,
            Choices: [.. Enum.GetValues<TEnum>().Select(v => new ImportChoice(v.ToString(), labelPrefix + v))]);
}

/// <summary>Writes a batch of validated employee rows in one transaction (D35); known TCKNs update their person.</summary>
public sealed record ImportEmployeesCommand(IReadOnlyList<ImportRow> Rows, bool UpdatesExisting) : IImportBatchCommand;

/// <summary>A person the rows name by TCKN, with the companies of their open employments.</summary>
internal sealed record ImportedEmployee(EmployeeId Id, IReadOnlyList<Guid> OpenCompanyIds);

/// <summary>A position of a company the rows may hire into.</summary>
internal sealed record ImportPosition(PositionId Id, PositionStatus Status, JobId JobId, string? JobLevel, string? CostCenter);

/// <summary>Companies, their positions and active branches, and the persons the rows refer to, loaded once.</summary>
internal sealed class EmployeeImportLookup
{
    private Dictionary<(Guid, string), ImportPosition> _positions = [];
    private Dictionary<(Guid, string), Guid> _branches = [];
    private Dictionary<string, ImportedEmployee> _employees = [];

    public Dictionary<string, CompanySummary> Companies { get; private set; } = [];

    public static async Task<EmployeeImportLookup> LoadAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        IReadOnlyList<ImportRow> rows,
        CancellationToken cancellationToken)
    {
        var companies = await HrImports.CompaniesAsync(organization, rows, EmployeeImport.Columns.Company, cancellationToken);
        var companyIds = companies.Values.Select(c => c.Id).ToList();
        var tcknList = rows.Select(r => r.Text(EmployeeImport.Columns.Tckn)?.Trim()).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var positions = await db.Positions.AsNoTracking()
            .Where(p => companyIds.Contains(p.CompanyId))
            .Select(p => new
            {
                p.CompanyId,
                p.Code,
                Position = new ImportPosition(
                    p.Id,
                    p.Status,
                    p.JobId,
                    db.Jobs.Where(j => j.Id == p.JobId).Select(j => j.Level).First(),
                    db.OrgUnits.Where(u => u.Id == p.OrgUnitId).Select(u => u.CostCenterCode).First()),
            })
            .ToListAsync(cancellationToken);
        var employees = await db.Employees.AsNoTracking()
            .Where(e => tcknList.Contains(e.Tckn))
            .Select(e => new { e.Tckn, e.Id, Open = e.Employments.Where(m => m.TerminationDate == null).Select(m => m.CompanyId).ToList() })
            .ToListAsync(cancellationToken);
        var branches = await organization.GetBranchesOfCompaniesAsync(companyIds, cancellationToken);
        return new EmployeeImportLookup
        {
            Companies = companies,
            _positions = positions.ToDictionary(p => (p.CompanyId, p.Code), p => p.Position),
            _branches = branches.Where(b => b.IsActive).ToDictionary(b => (b.CompanyId, b.Code), b => b.Id),
            _employees = employees.ToDictionary(e => e.Tckn, e => new ImportedEmployee(e.Id, e.Open), StringComparer.Ordinal),
        };
    }

    public ImportPosition? Position(Guid companyId, string? code) => code is null ? null : _positions.GetValueOrDefault((companyId, code));

    public Guid? Branch(Guid companyId, string code) => _branches.TryGetValue((companyId, code), out var id) ? id : null;

    public ImportedEmployee? Employee(string tckn) => _employees.GetValueOrDefault(tckn);

    /// <summary>A person hired by the batch, so that the same TCKN further down is not hired again.</summary>
    public void Add(string tckn, Employee employee) =>
        _employees[tckn] = new ImportedEmployee(employee.Id, [.. employee.Employments.Select(m => m.CompanyId)]);
}

/// <summary>Reading a row: the person, the contacts, and a hire checked against the domain's rules.</summary>
internal static class EmployeeImportRows
{
    /// <summary>Checks of the person and contact cells that the cell types do not cover.</summary>
    public static void CheckPerson(ImportRow row, ImportErrorList errors)
    {
        if (row.Text(EmployeeImport.Columns.Nationality) is { } nationality && !(nationality.Trim().Length == 2 && nationality.Trim().All(char.IsAsciiLetter)))
        {
            errors.Add(row, EmployeeImport.Columns.Nationality, CommonErrors.InvalidCountryCode.Code);
        }

        if (row.Text(EmployeeImport.Columns.Email) is { } email && SharedKernel.ValueObjects.Email.Create(email) is { IsFailure: true } badEmail)
        {
            errors.Add(row, EmployeeImport.Columns.Email, badEmail.Error.Code, badEmail.Error.Args);
        }

        if (row.Text(EmployeeImport.Columns.Phone) is { } phone && PhoneNumber.Create(phone) is { IsFailure: true } badPhone)
        {
            errors.Add(row, EmployeeImport.Columns.Phone, badPhone.Error.Code, badPhone.Error.Args);
        }
    }

    /// <summary>The person of a row; for a known person, blank cells keep the current values.</summary>
    public static PersonProfile Person(ImportRow row, Employee? current = null) =>
        new(
            row.Text(EmployeeImport.Columns.FirstName)!.Trim(),
            row.Text(EmployeeImport.Columns.LastName)!.Trim(),
            row.AsDate(EmployeeImport.Columns.BirthDate) ?? current?.BirthDate,
            row.Text(EmployeeImport.Columns.BirthPlace)?.Trim() ?? current?.BirthPlace,
            Parse<Gender>(row, EmployeeImport.Columns.Gender) ?? current?.Gender,
            row.Text(EmployeeImport.Columns.Nationality)?.Trim().ToUpperInvariant() ?? current?.Nationality,
            Parse<MaritalStatus>(row, EmployeeImport.Columns.MaritalStatus) ?? current?.MaritalStatus,
            Parse<MilitaryStatus>(row, EmployeeImport.Columns.MilitaryStatus) ?? current?.MilitaryStatus);

    public static string? EmailOf(ImportRow row) =>
        row.Text(EmployeeImport.Columns.Email) is { } email ? Email.Create(email).Value.Value : null;

    public static string? PhoneOf(ImportRow row) =>
        row.Text(EmployeeImport.Columns.Phone) is { } phone ? PhoneNumber.Create(phone).Value.Value : null;

    /// <summary>
    /// The hire of a new person on the row, through <see cref="Employee.Hire"/> so the domain's rules apply: the
    /// employment terms and the position, manager position and branch of the company. Not saved.
    /// </summary>
    public static Result<Employee> Hire(ImportRow row, Guid companyId, EmployeeImportLookup lookup, string registryNumber = "-")
    {
        var errors = new List<FieldError>();
        var hireDate = row.AsDate(EmployeeImport.Columns.HireDate);
        var type = Parse<EmploymentType>(row, EmployeeImport.Columns.EmploymentType);
        var positionCode = HrImports.Code(row.Text(EmployeeImport.Columns.Position));
        foreach (var (value, field) in new (object?, string)[] { (hireDate, "hireDate"), (type, "employmentType"), (positionCode, "positionId") })
        {
            if (value is null)
            {
                errors.Add(new FieldError(field, HrImportErrors.RequiredForNewCode));
            }
        }

        var position = lookup.Position(companyId, positionCode);
        if (positionCode is not null && position is not { Status: PositionStatus.Active })
        {
            errors.Add(new FieldError("positionId", EmployeeCommandErrors.InvalidPositionCode));
        }

        var managerCode = HrImports.Code(row.Text(EmployeeImport.Columns.ManagerPosition));
        var manager = lookup.Position(companyId, managerCode);
        if (managerCode is not null && (manager is null || manager.Status == PositionStatus.Closed || manager.Id == position?.Id))
        {
            errors.Add(new FieldError("managerPositionId", EmployeeCommandErrors.InvalidManagerPositionCode));
        }

        Guid? branchId = null;
        if (HrImports.Code(row.Text(EmployeeImport.Columns.Branch)) is { } branchCode && (branchId = lookup.Branch(companyId, branchCode)) is null)
        {
            errors.Add(new FieldError("branchId", OrgErrors.UnknownBranchCode));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var level = row.Text(EmployeeImport.Columns.Level)?.Trim();
        var costCenter = row.Text(EmployeeImport.Columns.CostCenter)?.Trim();
        var terms = new EmploymentTerms(
            companyId,
            hireDate!.Value,
            row.AsDate(EmployeeImport.Columns.SeniorityDate),
            row.Text(EmployeeImport.Columns.SeniorityReason)?.Trim(),
            type!.Value,
            row.AsDate(EmployeeImport.Columns.ContractEndDate),
            row.AsDecimal(EmployeeImport.Columns.WeeklyHours),
            row.AsDate(EmployeeImport.Columns.ProbationEndDate),
            null,
            null);
        var assignment = new AssignmentDetails(
            position!.Id,
            branchId,
            manager?.Id,
            position.JobId,
            string.IsNullOrWhiteSpace(level) ? position.JobLevel : level,
            string.IsNullOrWhiteSpace(costCenter) ? position.CostCenter : costCenter);
        var tckn = Tckn.Create(row.Text(EmployeeImport.Columns.Tckn));
        return tckn.IsFailure
            ? Error.Validation([new FieldError("tckn", tckn.Error.Code)])
            : Employee.Hire(tckn.Value, Person(row), terms, assignment, registryNumber);
    }

    /// <summary>The import column of a hire's field error.</summary>
    public static string? Column(string field) => field switch
    {
        "hireDate" => EmployeeImport.Columns.HireDate,
        "employmentType" => EmployeeImport.Columns.EmploymentType,
        "positionId" => EmployeeImport.Columns.Position,
        "managerPositionId" => EmployeeImport.Columns.ManagerPosition,
        "branchId" => EmployeeImport.Columns.Branch,
        "seniorityDate" => EmployeeImport.Columns.SeniorityDate,
        "seniorityReason" => EmployeeImport.Columns.SeniorityReason,
        "probationEndDate" => EmployeeImport.Columns.ProbationEndDate,
        "contractEndDate" => EmployeeImport.Columns.ContractEndDate,
        "weeklyHours" => EmployeeImport.Columns.WeeklyHours,
        _ => null,
    };

    /// <summary>The current list with <paramref name="value"/> as its primary item, added when missing.</summary>
    public static List<T> WithPrimary<T>(
        IReadOnlyList<T> current,
        string? value,
        Func<T, string> valueOf,
        Func<T, bool, T> withPrimary,
        Func<string, T> create)
    {
        if (value is null)
        {
            return [.. current];
        }

        var match = current.FirstOrDefault(i => string.Equals(valueOf(i), value, StringComparison.OrdinalIgnoreCase));
        return [withPrimary(match ?? create(value), true), .. current.Where(i => !ReferenceEquals(i, match)).Select(i => withPrimary(i, false))];
    }

    private static TEnum? Parse<TEnum>(ImportRow row, string column)
        where TEnum : struct, Enum =>
        row.Text(column) is { } text && Enum.TryParse<TEnum>(text, out var value) ? value : null;
}

/// <summary>The custom fields of new employees per company, with no values given: required ones without a default fail.</summary>
internal sealed class CustomFieldDefaults(ICustomFieldValidator customFields)
{
    private readonly Dictionary<Guid, Result<CustomFieldValues>> _results = [];

    public async Task<Result<CustomFieldValues>> ValidateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!_results.TryGetValue(companyId, out var result))
        {
            result = await customFields.ValidateAsync(HrCustomFieldEntities.Employee, companyId, null, CustomFieldValues.Empty, cancellationToken);
            _results[companyId] = result;
        }

        return result;
    }
}

internal sealed class ImportEmployeesCommandHandler(
    IHrDbContext db,
    IOrganizationLookup organization,
    ICustomFieldValidator customFields,
    INumberGenerator numbers,
    IIntegrationEventPublisher publisher) : ICommandHandler<ImportEmployeesCommand>
{
    public async Task<Result> Handle(ImportEmployeesCommand request, CancellationToken cancellationToken)
    {
        var lookup = await EmployeeImportLookup.LoadAsync(db, organization, request.Rows, cancellationToken);
        var fieldRules = new CustomFieldDefaults(customFields);
        var errors = new List<FieldError>();
        foreach (var row in request.Rows)
        {
            // The rows were validated; what changed since still fails the batch
            var tckn = Tckn.Create(row.Text(EmployeeImport.Columns.Tckn));
            var company = HrImports.Code(row.Text(EmployeeImport.Columns.Company)) is { } companyCode ? lookup.Companies.GetValueOrDefault(companyCode) : null;
            if (tckn.IsFailure || company is null)
            {
                errors.Add(HrImports.BatchError(row, null, CommonErrors.ValidationCode));
                continue;
            }

            if (lookup.Employee(tckn.Value.Value) is { } existing)
            {
                if (await UpdateAsync(request, row, existing, company.Id, cancellationToken) is { } updateError)
                {
                    errors.Add(updateError);
                }

                continue;
            }

            var values = await fieldRules.ValidateAsync(company.Id, cancellationToken);
            var registryNumber = await numbers.NextAsync(company.Id, HrNumberSequences.Employee, row.AsDate(EmployeeImport.Columns.HireDate) ?? default, cancellationToken);
            var hired = EmployeeImportRows.Hire(row, company.Id, lookup, registryNumber);
            if (hired.IsFailure || values.IsFailure)
            {
                errors.AddRange((hired.IsFailure ? hired.Error.Fields : values.Error.Fields)
                    .Select(f => HrImports.BatchError(row, EmployeeImportRows.Column(f.Field), f.Code)));
                continue;
            }

            var employee = hired.Value;
            var email = EmployeeImportRows.EmailOf(row);
            var phone = EmployeeImportRows.PhoneOf(row);
            employee.SetContacts(
                email is null ? [] : [new EmployeeEmail(ContactKind.Work, email, true)],
                phone is null ? [] : [new EmployeePhone(ContactKind.Mobile, phone, true)],
                [],
                []);
            employee.SetCustomFields(values.Value);
            var employment = employee.Employments[0];
            var first = employment.Assignments[0];
            employee.MarkAnnounced(first.Id);
            db.Employees.Add(employee);
            lookup.Add(tckn.Value.Value, employee);
            await publisher.PublishAsync(
                new EmployeeHired(employee.Id.Value, employment.Id.Value, employment.RegistryNumber, employment.HireDate, first.PositionId.Value, employee.UserId)
                {
                    CompanyId = employment.CompanyId,
                },
                cancellationToken);
        }

        return errors.Count > 0 ? Error.Validation(errors) : Result.Success();
    }

    /// <summary>A known person: the person and the primary e-mail and phone; only in update mode, with an open employment in the company.</summary>
    private async Task<FieldError?> UpdateAsync(ImportEmployeesCommand request, ImportRow row, ImportedEmployee existing, Guid companyId, CancellationToken cancellationToken)
    {
        if (!request.UpdatesExisting)
        {
            return HrImports.BatchError(row, EmployeeImport.Columns.Tckn, EmployeeCommandErrors.TcknExistsCode);
        }

        var employee = await EmployeeRules.LoadAsync(db, existing.Id.Value, cancellationToken);
        if (employee is null || !employee.Employments.Any(m => m.CompanyId == companyId && m.IsOpen))
        {
            return HrImports.BatchError(row, EmployeeImport.Columns.Company, HrImportErrors.NoOpenEmploymentCode);
        }

        var emails = EmployeeImportRows.WithPrimary(employee.Emails, EmployeeImportRows.EmailOf(row), e => e.Address, (e, primary) => e with { IsPrimary = primary }, a => new EmployeeEmail(ContactKind.Work, a, true));
        var phones = EmployeeImportRows.WithPrimary(employee.Phones, EmployeeImportRows.PhoneOf(row), p => p.Number, (p, primary) => p with { IsPrimary = primary }, n => new EmployeePhone(ContactKind.Mobile, n, true));
        if (emails.Count > Employee.MaxListItems || phones.Count > Employee.MaxListItems)
        {
            return HrImports.BatchError(row, null, "hr.employees.errors.too_many_items");
        }

        employee.UpdatePerson(EmployeeImportRows.Person(row, employee));
        employee.SetContacts(emails, phones, employee.Addresses, employee.EmergencyContacts);
        return null;
    }
}
