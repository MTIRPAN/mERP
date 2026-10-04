using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>Turkish national identity number (T.C. Kimlik No): 11 digits with two check digits.</summary>
public sealed record Tckn
{
    public const int Length = 11;

    private Tckn(string value) => Value = value;

    public string Value { get; }

    public static Result<Tckn> Create(string? value)
    {
        var trimmed = value?.Trim();
        return IsValid(trimmed) ? new Tckn(trimmed!) : CommonErrors.InvalidTckn;
    }

    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Length || value[0] == '0' || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        Span<int> d = stackalloc int[Length];
        for (var i = 0; i < Length; i++)
        {
            d[i] = value[i] - '0';
        }

        var oddSum = d[0] + d[2] + d[4] + d[6] + d[8];
        var evenSum = d[1] + d[3] + d[5] + d[7];
        var tenth = (((oddSum * 7) - evenSum) % 10 + 10) % 10;
        if (d[9] != tenth)
        {
            return false;
        }

        var firstTenSum = 0;
        for (var i = 0; i < 10; i++)
        {
            firstTenSum += d[i];
        }

        return d[10] == firstTenSum % 10;
    }

    /// <summary>Masked form for users without the sensitive data permission: <c>123******01</c>.</summary>
    public string Masked() => string.Concat(Value.AsSpan(0, 3), new string('*', 6), Value.AsSpan(9, 2));

    public override string ToString() => Value;
}
