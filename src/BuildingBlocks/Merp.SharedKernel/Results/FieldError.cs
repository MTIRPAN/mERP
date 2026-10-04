namespace Merp.SharedKernel.Results;

/// <summary>Validation error bound to an input field.</summary>
/// <param name="Field">Field path in camelCase, e.g. <c>address.city</c>.</param>
/// <param name="Code">Localization key, e.g. <c>common.errors.invalid_tckn</c>.</param>
/// <param name="Args">Values for the placeholders in the localized message.</param>
public sealed record FieldError(string Field, string Code, IReadOnlyDictionary<string, object?> Args)
{
    public FieldError(string field, string code)
        : this(field, code, Error.NoArgs)
    {
    }
}
