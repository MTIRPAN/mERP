using System.Collections.Concurrent;
using Merp.Modules.Platform.Application.Access;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.IntegrationTests.Employees;

/// <summary>Keycloak for the employee–user link tests (D39): identities are kept in memory.</summary>
public sealed class FakeIdentityAdmin : IIdentityAdmin
{
    public ConcurrentDictionary<string, NewIdentity> Identities { get; } = new();

    public ConcurrentDictionary<string, bool> Enabled { get; } = new();

    public Task<Result<string>> CreateUserAsync(NewIdentity identity, CancellationToken cancellationToken = default)
    {
        var subject = Guid.NewGuid().ToString();
        Identities[subject] = identity;
        Enabled[subject] = true;
        return Task.FromResult(Result.Success(subject));
    }

    public Task<Result> SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken = default)
    {
        Enabled[subject] = enabled;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SendActionsEmailAsync(string subject, IdentityEmail email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
