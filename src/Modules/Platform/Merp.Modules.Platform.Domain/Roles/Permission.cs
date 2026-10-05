namespace Merp.Modules.Platform.Domain.Roles;

/// <summary>
/// Catalog row of a permission defined in code (<c>{module}.{resource}.{action}</c>). Synchronized from the
/// modules' definitions after every migration; roles can only grant cataloged permissions.
/// </summary>
public sealed class Permission
{
    public const int CodeMaxLength = 100;

    private Permission()
    {
    }

    public Permission(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Module = code.Split('.')[0];
    }

    public string Code { get; private set; } = null!;

    public string Module { get; private set; } = null!;
}
