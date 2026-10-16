using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>Error codes of units, jobs and positions; texts in the module's <c>tr-TR.json</c>.</summary>
public static class OrgErrors
{
    public const string UnknownCompanyCode = "hr.org.errors.unknown_company";
    public const string CompanyInactiveCode = "hr.org.errors.company_inactive";
    public const string UnknownBranchCode = "hr.org.errors.unknown_branch";
    public const string ReportingCycleCode = "hr.org.errors.reporting_cycle";
    public const string UnknownParentCode = "hr.org_units.errors.unknown_parent";
    public const string InvalidManagerPositionCode = "hr.org_units.errors.invalid_manager_position";
    public const string UnknownOrgUnitCode = "hr.positions.errors.unknown_org_unit";
    public const string OrgUnitInOtherCompanyCode = "hr.positions.errors.org_unit_in_other_company";
    public const string UnknownJobCode = "hr.positions.errors.unknown_job";
    public const string JobInactiveCode = "hr.positions.errors.job_inactive";
    public const string InvalidReportsToCode = "hr.positions.errors.invalid_reports_to";
    public const string ManagesUnitCode = "hr.positions.errors.manages_unit";

    /// <summary>The job catalog is shared by the companies, so only the Tenant scope manages it (D37).</summary>
    public static readonly Error JobsNeedTenantScope = Error.Forbidden("hr.jobs.errors.tenant_scope_required");

    public static FieldError Duplicate(string field) => new(field, CommonErrors.Duplicate.Code);
}
