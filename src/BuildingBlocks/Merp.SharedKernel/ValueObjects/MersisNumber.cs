using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>Central registry number of a Turkish company (MERSİS numarası): 16 digits.</summary>
public sealed record MersisNumber
{
    public const int Length = 16;

    private MersisNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<MersisNumber> Create(string? value)
    {
        var digits = DigitInput.Strip(value);
        return digits?.Length == Length && digits.All(char.IsAsciiDigit) ? new MersisNumber(digits) : CommonErrors.InvalidMersisNumber;
    }

    public override string ToString() => Value;
}
