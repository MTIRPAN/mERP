using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>Turkish tax identification number (Vergi Kimlik No): 10 digits, the last one is a check digit.</summary>
public sealed record Vkn
{
    public const int Length = 10;

    private Vkn(string value) => Value = value;

    public string Value { get; }

    public static Result<Vkn> Create(string? value)
    {
        var trimmed = value?.Trim();
        return IsValid(trimmed) ? new Vkn(trimmed!) : CommonErrors.InvalidVkn;
    }

    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Length || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var total = 0;
        for (var i = 0; i < 9; i++)
        {
            var tmp = (value[i] - '0' + 9 - i) % 10;
            total += tmp == 9 ? 9 : tmp * (1 << (9 - i)) % 9;
        }

        var check = (10 - (total % 10)) % 10;
        return value[9] - '0' == check;
    }

    public override string ToString() => Value;
}
