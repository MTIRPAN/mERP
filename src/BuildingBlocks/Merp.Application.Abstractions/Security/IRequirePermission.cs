namespace Merp.Application.Abstractions.Security;

/// <summary>Request that requires a permission; checked by the authorization pipeline behavior.</summary>
public interface IRequirePermission
{
    /// <summary>Permission code, <c>{module}.{resource}.{action}</c>.</summary>
    string Permission { get; }
}
