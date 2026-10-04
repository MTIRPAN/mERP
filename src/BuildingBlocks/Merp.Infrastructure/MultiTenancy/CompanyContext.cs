using Merp.Application.Abstractions.Context;

namespace Merp.Infrastructure.MultiTenancy;

/// <summary>Scoped holder of the active company from the <c>X-Company-Id</c> header (D4).</summary>
public sealed class CompanyContext : ICompanyContext
{
    public Guid? ActiveCompanyId { get; private set; }

    public void Set(Guid? companyId) => ActiveCompanyId = companyId;
}
