using Merp.Application.Abstractions.Security;

namespace Merp.Infrastructure.Security;

/// <summary>Default until role assignments exist (PLAN P2): no data is in scope. Hosts may register another provider.</summary>
internal sealed class NoAccessScopeProvider : IAccessScopeProvider
{
    public Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccessScope>>([]);
}
