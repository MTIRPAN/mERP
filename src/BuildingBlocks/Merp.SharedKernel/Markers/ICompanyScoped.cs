namespace Merp.SharedKernel.Markers;

/// <summary>Record that belongs to one company of the tenant (D4). The id is a plain Guid owned by Platform.</summary>
public interface ICompanyScoped
{
    Guid CompanyId { get; }
}
