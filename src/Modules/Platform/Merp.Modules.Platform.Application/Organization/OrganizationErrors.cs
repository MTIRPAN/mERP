using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Application.Organization;

/// <summary>Error codes of companies, branches and locations; texts in the module's <c>tr-TR.json</c>.</summary>
public static class OrganizationErrors
{
    public const string UnknownProvinceCode = "platform.locations.errors.unknown_province";
    public const string UnknownDistrictCode = "platform.locations.errors.unknown_district";
    public const string UnknownCompanyCode = "platform.branches.errors.unknown_company";
    public const string HeadOfficeExistsCode = "platform.branches.errors.head_office_exists";
    public const string CompanyInactiveCode = "platform.branches.errors.company_inactive";

    /// <summary>Creating a company needs the Tenant scope; a company scope only covers existing companies.</summary>
    public static readonly Error CompanyCreationNeedsTenantScope = Error.Forbidden("platform.companies.errors.tenant_scope_required");

    public static FieldError Duplicate(string field) => new(field, CommonErrors.Duplicate.Code);
}
