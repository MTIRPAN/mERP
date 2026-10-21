using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>The SGK leaving codes of the tenant, by code; only the active ones unless asked (D38).</summary>
public sealed record GetSgkExitCodesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<SgkExitCodeItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Read;
}

/// <remarks>Name is in the caller's language.</remarks>
public sealed record SgkExitCodeItem(Guid Id, string Code, string Name, bool IsActive);

internal sealed class GetSgkExitCodesQueryHandler(IHrDbContext db, ICurrentUser currentUser) : IQueryHandler<GetSgkExitCodesQuery, IReadOnlyList<SgkExitCodeItem>>
{
    public async Task<Result<IReadOnlyList<SgkExitCodeItem>>> Handle(GetSgkExitCodesQuery request, CancellationToken cancellationToken)
    {
        var codes = await db.SgkExitCodes.AsNoTracking()
            .Where(c => request.IncludeInactive || c.IsActive)
            .ToListAsync(cancellationToken);
        return codes
            .OrderBy(c => c.Code.Length)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => new SgkExitCodeItem(c.Id.Value, c.Code, c.Name.Get(currentUser.Culture, "tr-TR"), c.IsActive))
            .ToList();
    }
}
