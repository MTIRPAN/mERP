using Merp.Application.Abstractions.Exports;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// Columns of the employee list that can be exported (D35). Keys and headers are those of the list columns on the
/// screen. The TCKN stays masked, as in the list.
/// </summary>
public static class EmployeeExports
{
    public static readonly IReadOnlyList<ExportColumn<EmployeeListItem>> Columns =
    [
        ExportColumn<EmployeeListItem>.Of("registryNumber", "web.hr.employees.fields.registry_number", e => e.RegistryNumber),
        ExportColumn<EmployeeListItem>.Of("firstName", "web.hr.employees.fields.first_name", e => e.FirstName),
        ExportColumn<EmployeeListItem>.Of("lastName", "web.hr.employees.fields.last_name", e => e.LastName),
        ExportColumn<EmployeeListItem>.Of("tckn", "web.hr.employees.fields.tckn", e => e.Tckn),
        ExportColumn<EmployeeListItem>.Of("companyName", "web.hr.employees.fields.company", e => e.CompanyName),
        ExportColumn<EmployeeListItem>.Of("branchName", "web.hr.employees.fields.branch", e => e.BranchName),
        ExportColumn<EmployeeListItem>.Of("orgUnitName", "web.hr.employees.fields.org_unit", e => e.OrgUnitName),
        ExportColumn<EmployeeListItem>.Of("positionCode", "web.hr.employees.fields.position", e => e.PositionCode),
        ExportColumn<EmployeeListItem>.Of("jobName", "web.hr.employees.fields.job", e => e.JobName),
        ExportColumn<EmployeeListItem>.Of("hireDate", "web.hr.employees.fields.hire_date", e => e.HireDate),
        ExportColumn<EmployeeListItem>.Of("terminationDate", "web.hr.employees.fields.termination_date", e => e.TerminationDate),
        ExportColumn<EmployeeListItem>.Of("status", "web.hr.employees.fields.status", e => e.Status, "web.hr.employment_status."),
    ];
}
