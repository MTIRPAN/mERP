namespace Merp.Application.Abstractions.Security;

/// <summary>Checks whether the current user holds a permission through at least one valid role assignment.</summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default);
}
