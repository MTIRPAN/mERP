using Microsoft.AspNetCore.Builder;

namespace Merp.Web.Security;

/// <summary>Marks an endpoint that serves the caller's own data and needs a signed-in user but no permission (D22).</summary>
public sealed record AuthenticatedUserOnly;

public static class AuthenticatedUserEndpointExtensions
{
    /// <summary>Signed-in user of a resolved tenant; no permission. Allowed only for routes listed in D22.</summary>
    public static TBuilder RequireAuthenticatedUser<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().WithMetadata(new AuthenticatedUserOnly());
}
