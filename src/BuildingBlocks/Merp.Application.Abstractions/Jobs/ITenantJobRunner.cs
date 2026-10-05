namespace Merp.Application.Abstractions.Jobs;

/// <summary>
/// Runs background work per tenant (D3). Each invocation gets its own DI scope with the tenant context set and the
/// caller marked as system.
/// </summary>
public interface ITenantJobRunner
{
    /// <summary>Runs <paramref name="work"/> once for every active tenant. A failure in one tenant does not stop the others.</summary>
    Task RunForEachTenantAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work"/> for one tenant.</summary>
    Task RunForTenantAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken);
}
