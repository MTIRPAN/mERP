namespace Merp.Modules.HR.Domain;

/// <summary>
/// Guards for free text set by domain methods. Input is validated by the application layer first; these guards
/// stop programming errors, they are not user facing validation.
/// </summary>
internal static class Text
{
    public static string Required(string? value, int maxLength, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return trimmed.Length <= maxLength ? trimmed : throw new ArgumentException($"{name} has at most {maxLength} characters.", name);
    }

    public static string? Optional(string? value, int maxLength, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maxLength, name);
}
