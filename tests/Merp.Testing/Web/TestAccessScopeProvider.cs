using Merp.Application.Abstractions.Security;
using Merp.Web.Security;
using Microsoft.AspNetCore.Http;

namespace Merp.Testing.Web;

/// <summary>
/// The caller has the scopes in the <see cref="ScopesHeader"/> request header (<c>Company:{id},Branch:{id}</c>) for
/// every permission granted by <see cref="TestPermissionChecker"/>; without the header the scope is Tenant.
/// </summary>
public sealed class TestAccessScopeProvider(IHttpContextAccessor httpContextAccessor, IPermissionChecker permissionChecker) : IAccessScopeProvider
{
    public const string ScopesHeader = "X-Test-Scopes";

    public Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scopes of other users need role assignments (PLAN P2).");

    public async Task<IReadOnlyList<AccessScope>> GetCurrentUserScopesAsync(string permission, CancellationToken cancellationToken = default)
    {
        if (!await permissionChecker.HasPermissionAsync(permission, cancellationToken))
        {
            return [];
        }

        var header = httpContextAccessor.HttpContext?.Request.Headers[ScopesHeader].ToString();
        return string.IsNullOrWhiteSpace(header)
            ? [AccessScope.Tenant]
            : AccessScopeParser.Parse(header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

public static class TestScopeHttpClientExtensions
{
    /// <summary>Restricts the client's data scope, e.g. <c>client.WithScopes(AccessScope.Company(id))</c>.</summary>
    public static HttpClient WithScopes(this HttpClient client, params AccessScope[] scopes)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Remove(TestAccessScopeProvider.ScopesHeader);
        client.DefaultRequestHeaders.Add(
            TestAccessScopeProvider.ScopesHeader,
            string.Join(',', scopes.Select(s => s.ScopeId is null ? s.Type : $"{s.Type}:{s.ScopeId}")));
        return client;
    }
}
