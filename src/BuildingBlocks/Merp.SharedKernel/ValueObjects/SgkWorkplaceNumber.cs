using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Social security workplace registration number (SGK işyeri sicil numarası): 26 digits. Spaces, dots and dashes
/// used to group the digits are ignored.
/// </summary>
public sealed record SgkWorkplaceNumber
{
    public const int Length = 26;

    private SgkWorkplaceNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<SgkWorkplaceNumber> Create(string? value)
    {
        var digits = DigitInput.Strip(value);
        return digits?.Length == Length && digits.All(char.IsAsciiDigit)
            ? new SgkWorkplaceNumber(digits)
            : CommonErrors.InvalidSgkWorkplaceNumber;
    }

    public override string ToString() => Value;
}

internal static class DigitInput
{
    /// <summary>Removes grouping characters (space, dot, dash) people type into long numbers.</summary>
    public static string? Strip(string? value) =>
        value is null ? null : string.Concat(value.Where(c => c is not (' ' or '.' or '-')));
}
