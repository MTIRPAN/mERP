using Merp.Application.Abstractions.Security;
using Microsoft.AspNetCore.Http;

namespace Merp.Testing.Web;

/// <summary>Grants the permissions listed in the <see cref="PermissionsHeader"/> request header (comma separated).</summary>
public sealed class TestPermissionChecker(IHttpContextAccessor httpContextAccessor) : IPermissionChecker
{
    public const string PermissionsHeader = "X-Test-Permissions";

    public Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers[PermissionsHeader].ToString() ?? string.Empty;
        var granted = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Task.FromResult(granted.Contains(permission, StringComparer.Ordinal));
    }
}
