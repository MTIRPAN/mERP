namespace Merp.Application.Abstractions.Security;

/// <summary>
/// Maps the signed-in identity (Keycloak <c>sub</c>) to the tenant's platform user, creating it on first sign-in
/// (just-in-time provisioning, D6). Platform implements it; the web host calls it once per request.
/// </summary>
public interface ICurrentUserProvisioner
{
    Task<ProvisionedUser> ProvisionAsync(IdentityClaims identity, CancellationToken cancellationToken = default);
}

/// <summary>Claims of the access token that describe the person.</summary>
public sealed record IdentityClaims(string Subject, string? UserName, string? Email, string? GivenName, string? FamilyName, string? Name);

/// <param name="UserId">Platform user id.</param>
/// <param name="IsActive">Inactive users are refused even with a valid token.</param>
/// <param name="EmployeeId">HR employee linked to the user (D6).</param>
/// <param name="Culture">Preferred culture, e.g. <c>tr-TR</c>; null follows the request (D11).</param>
public sealed record ProvisionedUser(Guid UserId, bool IsActive, Guid? EmployeeId, string? Culture);
