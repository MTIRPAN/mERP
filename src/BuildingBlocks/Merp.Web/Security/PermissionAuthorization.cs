using Merp.Application.Abstractions.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Merp.Web.Security;

/// <summary>Endpoint metadata naming the permission an endpoint requires (CLAUDE.md rule 6).</summary>
public sealed record RequiredPermission(string Permission);

public static class PermissionEndpointExtensions
{
    /// <summary>
    /// Requires an authenticated user holding <paramref name="permission"/> through a valid role assignment
    /// (docs/domain/yetki-ve-onay.md §1.3). Missing permission → 403.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return builder
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + permission)
            .WithMetadata(new RequiredPermission(permission));
    }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Creates <c>permission:{code}</c> policies on demand.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "permission:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[PolicyPrefix.Length..]))
            .Build();
    }
}

/// <summary>Checks the permission through <see cref="IPermissionChecker"/> in the request scope.</summary>
public sealed class PermissionAuthorizationHandler(IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        var services = httpContextAccessor.HttpContext?.RequestServices;
        if (services is null)
        {
            return;
        }

        var checker = services.GetRequiredService<IPermissionChecker>();
        if (await checker.HasPermissionAsync(requirement.Permission, httpContextAccessor.HttpContext!.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
