using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merp.Testing.Web;

/// <summary>
/// Authenticates test requests from headers instead of Keycloak tokens. Requests without
/// <see cref="SubjectHeader"/> are anonymous.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubjectHeader = "X-Test-Sub";
    public const string TenantHeader = "X-Test-Tenant";

    /// <summary>Optional e-mail address of the user, as Keycloak puts it in the token (notifications, PLAN P8).</summary>
    public const string EmailHeader = "X-Test-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubjectHeader, out var subject) || string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", subject.ToString()), new(ClaimTypes.NameIdentifier, subject.ToString()) };
        if (Request.Headers.TryGetValue(TenantHeader, out var tenant) && !string.IsNullOrWhiteSpace(tenant))
        {
            claims.Add(new Claim("tenant", tenant.ToString()));
        }

        if (Request.Headers.TryGetValue(EmailHeader, out var email) && !string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim("email", email.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
