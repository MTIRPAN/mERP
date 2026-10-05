using Merp.Application.Abstractions.Security;
using Merp.Web.Security;
using Microsoft.AspNetCore.Http;

namespace Merp.Testing.Web;

/// <summary>
/// Data scopes from the <see cref="ScopesHeader"/> request header (comma separated, e.g. <c>Company:{id},Branch:{id}</c>).
/// Without the header the caller sees the whole tenant.
/// </summary>
public sealed class TestAccessScopeProvider(IHttpContextAccessor httpContextAccessor) : IAccessScopeProvider
{
    public const string ScopesHeader = "X-Test-Scopes";

    /// <summary>Header value for a caller without any scope.</summary>
    public const string NoScopes = "none";

    public Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var headers = httpContextAccessor.HttpContext?.Request.Headers;
        return Task.FromResult(headers is not null && headers.TryGetValue(ScopesHeader, out var value)
            ? AccessScopeParser.Parse(value.ToString().Split(',').Where(v => !string.Equals(v, NoScopes, StringComparison.Ordinal)))
            : AccessScopeParser.TenantWide);
    }
}

public static class TestHttpClientExtensions
{
    /// <summary>
    /// Limits the client's data scopes, e.g. <c>WithScopes($"Company:{id}")</c>; without arguments the client has
    /// no scope at all.
    /// </summary>
    public static HttpClient WithScopes(this HttpClient client, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Remove(TestAccessScopeProvider.ScopesHeader);
        // An empty header value is not sent, so "no scope" travels as a value that parses to nothing.
        client.DefaultRequestHeaders.Add(TestAccessScopeProvider.ScopesHeader, scopes.Length == 0 ? TestAccessScopeProvider.NoScopes : string.Join(',', scopes));
        return client;
    }

    /// <summary>Sends the active company header (D4).</summary>
    public static HttpClient WithCompany(this HttpClient client, Guid companyId)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Remove("X-Company-Id");
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        return client;
    }
}
