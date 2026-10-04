using System.Text;
using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// International bank account number, validated with ISO 13616 mod-97. Turkish IBANs must have 26 characters.
/// Stored without spaces in upper case.
/// </summary>
public sealed record Iban
{
    public const int TurkishLength = 26;
    private const int MinLength = 15;
    private const int MaxLength = 34;

    private Iban(string value) => Value = value;

    public string Value { get; }

    public string CountryCode => Value[..2];

    public static Result<Iban> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CommonErrors.InvalidIban;
        }

        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return IsValid(normalized) ? new Iban(normalized) : CommonErrors.InvalidIban;
    }

    private static bool IsValid(string iban)
    {
        if (iban.Length is < MinLength or > MaxLength
            || !char.IsAsciiLetterUpper(iban[0]) || !char.IsAsciiLetterUpper(iban[1])
            || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3])
            || !iban.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        if (iban.StartsWith("TR", StringComparison.Ordinal) && (iban.Length != TurkishLength || !iban.AsSpan(4).ContainsOnlyDigits()))
        {
            return false;
        }

        // Move the first four characters to the end, map letters to 10..35 and compute mod 97 piecewise.
        var remainder = 0;
        foreach (var c in iban[4..] + iban[..4])
        {
            var number = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = number < 10 ? ((remainder * 10) + number) % 97 : ((remainder * 100) + number) % 97;
        }

        return remainder == 1;
    }

    /// <summary>Groups of four characters for display: <c>TR33 0006 1005 ...</c>.</summary>
    public string Formatted()
    {
        var builder = new StringBuilder(Value.Length + (Value.Length / 4));
        for (var i = 0; i < Value.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append(' ');
            }

            builder.Append(Value[i]);
        }

        return builder.ToString();
    }

    public override string ToString() => Value;
}

internal static class SpanExtensions
{
    public static bool ContainsOnlyDigits(this ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
