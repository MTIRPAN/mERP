namespace Merp.Web.Navigation;

/// <summary>
/// An entry of the application menu. Labels are <c>web.*</c> localization keys resolved by the web client; items
/// whose permission the user lacks are left out of <c>/api/platform/me</c>.
/// </summary>
public sealed record MenuItem(
    string Key,
    string LabelKey,
    string? Path = null,
    string? Icon = null,
    string? Permission = null,
    int Order = 0,
    IReadOnlyList<MenuItem>? Children = null);

/// <summary>Modules contribute their menu items through this interface.</summary>
public interface IMenuContributor
{
    IEnumerable<MenuItem> GetMenuItems();
}
