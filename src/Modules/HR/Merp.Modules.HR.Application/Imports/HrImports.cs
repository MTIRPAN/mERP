using System.Globalization;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Application.Imports;

/// <summary>Helpers shared by HR's imports (PLAN H4, D40).</summary>
internal static class HrImports
{
    /// <summary>
    /// The companies whose records the importing user may manage with <paramref name="permission"/>: the caller's own
    /// scopes while they wait, theirs by id when validated in the background (D35).
    /// </summary>
    public static async Task<OrgScope> CompanyScopeAsync(
        IAccessScopeProvider scopes,
        ICurrentUser currentUser,
        ImportContext context,
        string permission,
        CancellationToken cancellationToken) =>
        OrgScope.ForCompanies(await ScopesAsync(scopes, currentUser, context, permission, cancellationToken));

    public static Task<IReadOnlyList<AccessScope>> ScopesAsync(
        IAccessScopeProvider scopes,
        ICurrentUser currentUser,
        ImportContext context,
        string permission,
        CancellationToken cancellationToken) =>
        currentUser.UserId == context.UserId && !currentUser.IsSystem
            ? scopes.GetCurrentUserScopesAsync(permission, cancellationToken)
            : scopes.GetScopesAsync(context.UserId, permission, cancellationToken);

    /// <summary>A code as it is stored: trimmed and upper case; null when blank.</summary>
    public static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    /// <summary>The companies the rows name in <paramref name="column"/>, by code.</summary>
    public static async Task<Dictionary<string, CompanySummary>> CompaniesAsync(
        IOrganizationLookup organization,
        IEnumerable<ImportRow> rows,
        string column,
        CancellationToken cancellationToken)
    {
        var codes = rows.Select(r => Code(r.Text(column))).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return (await organization.FindCompaniesByCodeAsync(codes, cancellationToken)).ToDictionary(c => c.Code, StringComparer.Ordinal);
    }

    /// <summary>The field error of a batch row, <c>{row}.{column}</c>, as the import runner reads it (D35).</summary>
    public static FieldError BatchError(ImportRow row, string? column, string code) =>
        new(column is null ? row.Number.ToString(CultureInfo.InvariantCulture) : $"{row.Number}.{column}", code);
}

/// <summary>Errors of the rows, in the importing user's language.</summary>
internal sealed class ImportErrorList(ITextLocalizer localizer, string culture)
{
    private readonly List<ImportError> _errors = [];
    private readonly HashSet<int> _rows = [];

    public IReadOnlyList<ImportError> Items => _errors;

    public void Add(ImportRow row, string? column, string code, IReadOnlyDictionary<string, object?>? args = null)
    {
        _errors.Add(new ImportError(row.Number, column, localizer.Get(code, args, culture)));
        _rows.Add(row.Number);
    }

    /// <summary>A file-level problem, e.g. a missing scope; no row may be imported.</summary>
    public void AddForFile(string code) => _errors.Add(new ImportError(0, null, localizer.Get(code, culture: culture)));

    public bool HasErrors(ImportRow row) => _rows.Contains(row.Number);

    /// <summary>
    /// The company of a row: it exists, is active and is in the importing user's scope. Null with an error otherwise.
    /// </summary>
    public CompanySummary? Company(ImportRow row, string column, IReadOnlyDictionary<string, CompanySummary> companies, OrgScope scope)
    {
        var company = HrImports.Code(row.Text(column)) is { } code ? companies.GetValueOrDefault(code) : null;
        if (company is null)
        {
            Add(row, column, OrgErrors.UnknownCompanyCode);
            return null;
        }

        if (!scope.CoversCompany(company.Id))
        {
            Add(row, column, HrImportErrors.CompanyOutOfScopeCode);
            return null;
        }

        if (!company.IsActive)
        {
            Add(row, column, OrgErrors.CompanyInactiveCode);
            return null;
        }

        return company;
    }
}

/// <summary>Error codes of HR's imports; texts in the module's <c>tr-TR.json</c>.</summary>
public static class HrImportErrors
{
    public const string CompanyOutOfScopeCode = "hr.imports.errors.company_out_of_scope";
    public const string DuplicateInFileCode = "platform.imports.errors.duplicate_in_file";
    public const string LaterInFileCode = "hr.imports.errors.later_in_file";
    public const string UnitHasManagerCode = "hr.imports.positions.errors.unit_has_manager";
    public const string RequiredForNewCode = "hr.imports.employees.errors.required_for_new";
    public const string NoOpenEmploymentCode = "hr.imports.employees.errors.no_open_employment";
    public const string CustomFieldCode = "hr.imports.employees.errors.custom_field";
}
