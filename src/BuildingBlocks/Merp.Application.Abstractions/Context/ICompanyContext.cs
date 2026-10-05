namespace Merp.Application.Abstractions.Context;

/// <summary>
/// Active company chosen in the UI and sent in the <c>X-Company-Id</c> header (D4). It narrows lists and sets the
/// default company for new records. It never widens the caller's permissions.
/// </summary>
public interface ICompanyContext
{
    Guid? ActiveCompanyId { get; }
}
