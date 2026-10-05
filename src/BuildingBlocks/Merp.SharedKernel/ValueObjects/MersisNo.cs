using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>Central Registry Number (MERSİS no) of a Turkish company: 16 digits. Spaces and dashes are ignored.</summary>
public sealed record MersisNo
{
    public const int Length = 16;

    private MersisNo(string value) => Value = value;

    public string Value { get; }

    public static Result<MersisNo> Create(string? value)
    {
        var digits = DigitsOnly(value);
        return digits is { Length: Length } ? new MersisNo(digits) : CommonErrors.InvalidMersisNo;
    }

    public override string ToString() => Value;

    internal static string? DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = string.Concat(value.Where(c => c is not (' ' or '-' or '.')));
        return cleaned.All(char.IsAsciiDigit) ? cleaned : null;
    }
}
