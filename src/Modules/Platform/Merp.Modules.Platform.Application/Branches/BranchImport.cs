using System.Globalization;
using System.Text;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Branches;

/// <summary>
/// Branches from Excel (PLAN P11b, D35): the company by its code, the province and district by name (or the
/// province's plate number). Needs <c>platform.branches.manage</c> with a scope covering each row's company.
/// </summary>
public sealed class BranchImport(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    ITextLocalizer localizer) : IImportDefinition
{
    public const string ImportKey = "platform.branches";

    public static class Columns
    {
        public const string Company = "companyCode";
        public const string Code = "code";
        public const string Name = "name";
        public const string Type = "type";
        public const string Province = "province";
        public const string District = "district";
        public const string Address = "address";
        public const string PostalCode = "postalCode";
        public const string Phone = "phone";
        public const string SgkWorkplaceNumber = "sgkWorkplaceNumber";
    }

    public string Key => ImportKey;

    public string NameKey => "platform.imports.branches.name";

    public string Permission => PlatformPermissions.Branches.Manage;

    IReadOnlyList<ImportColumn> IImportDefinition.Columns { get; } =
    [
        new(Columns.Company, "platform.imports.branches.company", Required: true, HintKey: "platform.imports.branches.company_hint"),
        new(Columns.Code, "platform.imports.branches.code", Required: true, HintKey: "platform.imports.branches.code_hint", MaxLength: Branch.CodeMaxLength),
        new(Columns.Name, "platform.imports.branches.branch_name", Required: true, MaxLength: Branch.NameMaxLength),
        new(Columns.Type, "platform.imports.branches.type", ImportColumnType.Choice, Required: true,
            Choices: [.. Enum.GetValues<BranchType>().Select(t => new ImportChoice(t.ToString(), "web.platform.branch_type." + t))]),
        new(Columns.Province, "platform.imports.branches.province", Required: true, HintKey: "platform.imports.branches.province_hint"),
        new(Columns.District, "platform.imports.branches.district", Required: true),
        new(Columns.Address, "platform.imports.branches.address", Required: true, MaxLength: 250),
        new(Columns.PostalCode, "platform.imports.branches.postal_code", MaxLength: 10),
        new(Columns.Phone, "platform.imports.branches.phone"),
        new(Columns.SgkWorkplaceNumber, "platform.imports.branches.sgk"),
    ];

    public async Task<IReadOnlyList<ImportError>> ValidateAsync(IReadOnlyList<ImportRow> rows, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<ImportError>();
        void Add(ImportRow row, string column, string key, IReadOnlyDictionary<string, object?>? args = null) =>
            errors.Add(new ImportError(row.Number, column, localizer.Get(key, args, context.Culture)));

        // The user's scope: the caller's own while they wait, theirs by id when validated in the background
        var granted = currentUser.UserId == context.UserId && !currentUser.IsSystem
            ? await scopes.GetCurrentUserScopesAsync(Permission, cancellationToken)
            : await scopes.GetScopesAsync(context.UserId, Permission, cancellationToken);
        var scope = OrganizationScope.From(granted);
        var lookup = await BranchImportLookup.LoadAsync(db, rows, cancellationToken);

        var seen = new HashSet<(Guid, string)>();
        var headOffices = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var company = lookup.Company(row.Text(Columns.Company));
            if (company is null)
            {
                Add(row, Columns.Company, OrganizationErrors.UnknownCompanyCode);
            }
            else if (!scope.CoversCompany(company.Id))
            {
                Add(row, Columns.Company, "platform.imports.branches.errors.company_out_of_scope");
            }
            else if (!company.IsActive)
            {
                Add(row, Columns.Company, OrganizationErrors.CompanyInactiveCode);
            }

            var code = Code.Create(row.Text(Columns.Code), Branch.CodeMaxLength);
            if (code.IsFailure)
            {
                Add(row, Columns.Code, code.Error.Code, code.Error.Args);
            }
            else if (company is not null)
            {
                if (!seen.Add((company.Id.Value, code.Value.Value)))
                {
                    Add(row, Columns.Code, "platform.imports.errors.duplicate_in_file");
                }
                else if (lookup.BranchExists(company.Id, code.Value.Value))
                {
                    Add(row, Columns.Code, CommonErrors.Duplicate.Code);
                }
            }

            if (company is not null && row.Text(Columns.Type) == nameof(BranchType.HeadOffice)
                && (lookup.HasHeadOffice(company.Id) || !headOffices.Add(company.Id.Value)))
            {
                Add(row, Columns.Type, OrganizationErrors.HeadOfficeExistsCode);
            }

            var province = lookup.Province(row.Text(Columns.Province));
            if (province is null)
            {
                Add(row, Columns.Province, OrganizationErrors.UnknownProvinceCode);
            }
            else if (lookup.District(province.Value, row.Text(Columns.District)) is null)
            {
                Add(row, Columns.District, OrganizationErrors.UnknownDistrictCode);
            }

            if (row.Text(Columns.Phone) is { } phone && PhoneNumber.Create(phone) is { IsFailure: true } badPhone)
            {
                Add(row, Columns.Phone, badPhone.Error.Code, badPhone.Error.Args);
            }

            if (row.Text(Columns.SgkWorkplaceNumber) is { } sgk && SgkWorkplaceNumber.Create(sgk) is { IsFailure: true } badSgk)
            {
                Add(row, Columns.SgkWorkplaceNumber, badSgk.Error.Code, badSgk.Error.Args);
            }
        }

        return errors;
    }

    public IImportBatchCommand CreateBatch(IReadOnlyList<ImportRow> rows) => new ImportBranchesCommand(rows);
}

/// <summary>Writes a batch of validated branch rows in one transaction (D35).</summary>
public sealed record ImportBranchesCommand(IReadOnlyList<ImportRow> Rows) : IImportBatchCommand;

/// <summary>Companies, branch codes, provinces and districts the rows refer to, loaded once.</summary>
internal sealed class BranchImportLookup
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private Dictionary<string, Company> _companies = [];
    private HashSet<(Guid, string)> _branches = [];
    private HashSet<Guid> _headOffices = [];
    private Dictionary<string, int> _provinces = [];
    private Dictionary<(int, string), int> _districts = [];

    public static async Task<BranchImportLookup> LoadAsync(IPlatformDbContext db, IEnumerable<ImportRow> rows, CancellationToken cancellationToken)
    {
        var codes = rows.Select(r => r.Text(BranchImport.Columns.Company)?.Trim().ToUpperInvariant()).OfType<string>().Distinct().ToList();
        var companies = await db.Companies.AsNoTracking().Where(c => codes.Contains(c.Code)).ToListAsync(cancellationToken);
        var companyIds = companies.Select(c => c.Id).ToList();
        var branches = await db.Branches.AsNoTracking().Where(b => companyIds.Contains(b.CompanyId))
            .Select(b => new { b.CompanyId, b.Code, b.Type }).ToListAsync(cancellationToken);
        var provinces = await db.Provinces.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        var districts = await db.Districts.AsNoTracking().Select(d => new { d.Id, d.ProvinceId, d.Name }).ToListAsync(cancellationToken);

        var lookup = new BranchImportLookup
        {
            _companies = companies.ToDictionary(c => c.Code, StringComparer.Ordinal),
            _branches = [.. branches.Select(b => (b.CompanyId.Value, b.Code))],
            _headOffices = [.. branches.Where(b => b.Type == BranchType.HeadOffice).Select(b => b.CompanyId.Value)],
            _provinces = provinces.ToDictionary(p => Fold(p.Name), p => p.Id, StringComparer.Ordinal),
            _districts = districts.GroupBy(d => (d.ProvinceId, Fold(d.Name))).ToDictionary(g => g.Key, g => g.First().Id),
        };
        foreach (var province in provinces)
        {
            lookup._provinces[province.Id.ToString(CultureInfo.InvariantCulture)] = province.Id;
            lookup._provinces[province.Id.ToString("00", CultureInfo.InvariantCulture)] = province.Id;
        }

        return lookup;
    }

    public Company? Company(string? code) => code is null ? null : _companies.GetValueOrDefault(code.Trim().ToUpperInvariant());

    public bool BranchExists(CompanyId companyId, string code) => _branches.Contains((companyId.Value, code));

    public bool HasHeadOffice(CompanyId companyId) => _headOffices.Contains(companyId.Value);

    /// <summary>The province by name (any case, with or without Turkish letters) or plate number.</summary>
    public int? Province(string? name) => name is not null && _provinces.TryGetValue(Fold(name), out var id) ? id : null;

    public int? District(int province, string? name) => name is not null && _districts.TryGetValue((province, Fold(name)), out var id) ? id : null;

    /// <summary>"İstanbul", "ISTANBUL" and "istanbul" match; so do "Şişli" and "Sisli".</summary>
    private static string Fold(string text)
    {
        var upper = text.Trim().ToUpper(Turkish);
        var folded = new StringBuilder(upper.Length);
        foreach (var c in upper)
        {
            folded.Append(c switch
            {
                'İ' => 'I',
                'Ş' => 'S',
                'Ğ' => 'G',
                'Ü' => 'U',
                'Ö' => 'O',
                'Ç' => 'C',
                _ => c,
            });
        }

        return folded.ToString();
    }
}

internal sealed class ImportBranchesCommandHandler(
    IPlatformDbContext db,
    AddressResolver addressResolver,
    IIntegrationEventPublisher publisher,
    ICalendarCache calendarCache,
    IAfterCommit afterCommit) : ICommandHandler<ImportBranchesCommand>
{
    public async Task<Result> Handle(ImportBranchesCommand request, CancellationToken cancellationToken)
    {
        var lookup = await BranchImportLookup.LoadAsync(db, request.Rows, cancellationToken);
        var errors = new List<FieldError>();
        foreach (var row in request.Rows)
        {
            // The rows were validated; what changed since (a branch added by hand) still fails the batch
            var company = lookup.Company(row.Text(BranchImport.Columns.Company));
            var code = Code.Create(row.Text(BranchImport.Columns.Code), Branch.CodeMaxLength);
            var province = lookup.Province(row.Text(BranchImport.Columns.Province));
            if (company is null || code.IsFailure || province is null)
            {
                errors.Add(new FieldError(row.Number.ToString(CultureInfo.InvariantCulture), CommonErrors.ValidationCode));
                continue;
            }

            if (lookup.BranchExists(company.Id, code.Value.Value))
            {
                errors.Add(new FieldError($"{row.Number}.{BranchImport.Columns.Code}", CommonErrors.Duplicate.Code));
                continue;
            }

            var fields = new ImportedBranch(
                row.Text(BranchImport.Columns.Name),
                Enum.Parse<BranchType>(row.Text(BranchImport.Columns.Type)!),
                new AddressInput(
                    row.Text(BranchImport.Columns.Address),
                    ProvinceCode: province,
                    DistrictCode: lookup.District(province.Value, row.Text(BranchImport.Columns.District)),
                    PostalCode: row.Text(BranchImport.Columns.PostalCode)),
                row.Text(BranchImport.Columns.SgkWorkplaceNumber),
                row.Text(BranchImport.Columns.Phone));
            var profile = await BranchRules.ToProfileAsync(db, addressResolver, fields, company.Id, null, cancellationToken);
            if (profile.IsFailure)
            {
                errors.AddRange(profile.Error.Fields.Select(f => f with { Field = $"{row.Number}.{Column(f.Field)}" }));
                continue;
            }

            var branch = Branch.Create(company.Id, code.Value, profile.Value);
            db.Branches.Add(branch);
            await publisher.PublishAsync(BranchRules.ToCreatedEvent(branch), cancellationToken);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        afterCommit.Enqueue(calendarCache.InvalidateAsync);
        return Result.Success();
    }

    /// <summary>The import column of a branch field error.</summary>
    private static string Column(string field) => field switch
    {
        "type" => BranchImport.Columns.Type,
        "phone" => BranchImport.Columns.Phone,
        "sgkWorkplaceNumber" => BranchImport.Columns.SgkWorkplaceNumber,
        "address.provinceCode" => BranchImport.Columns.Province,
        "address.districtCode" => BranchImport.Columns.District,
        "address.postalCode" => BranchImport.Columns.PostalCode,
        _ when field.StartsWith("address", StringComparison.Ordinal) => BranchImport.Columns.Address,
        _ => BranchImport.Columns.Name,
    };

    private sealed record ImportedBranch(string? Name, BranchType? Type, AddressInput? Address, string? SgkWorkplaceNumber, string? Phone, Guid? WorkScheduleId = null)
        : IBranchFields;
}
