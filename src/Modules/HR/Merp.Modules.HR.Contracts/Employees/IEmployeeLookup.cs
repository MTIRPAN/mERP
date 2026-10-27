namespace Merp.Modules.HR.Contracts.Employees;

/// <summary>An employment of a person, for other modules (D48).</summary>
/// <remarks>TerminationDate is the last day; null while the employment is open.</remarks>
public sealed record EmployeeSummary(Guid EmployeeId, Guid EmploymentId, string FullName, string RegistryNumber, Guid CompanyId, DateOnly? TerminationDate);

/// <summary>How to reach a person: the primary e-mail and phone of the personnel record.</summary>
public sealed record EmployeeContact(Guid EmployeeId, string FirstName, string LastName, string? Email, string? Phone);

/// <summary>
/// People of a company for other modules, read only (D48): e.g. the employee a requisition replaces. Scopes are not
/// applied; the caller decides what to show and shows only names and registry numbers.
/// </summary>
public interface IEmployeeLookup
{
    /// <summary>Employments of the company whose name or registry number contains the text, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<EmployeeSummary>> SearchAsync(Guid companyId, string? search, int take, CancellationToken cancellationToken = default);

    /// <summary>The person's employment in the company; null when there is none.</summary>
    Task<EmployeeSummary?> FindAsync(Guid employeeId, Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>The person's name and primary contacts, e.g. for an internal application (D49); null when unknown.</summary>
    Task<EmployeeContact?> GetContactAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
