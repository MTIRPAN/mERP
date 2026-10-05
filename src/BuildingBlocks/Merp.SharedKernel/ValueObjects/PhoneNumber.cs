using System.Text;
using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Phone number in E.164 format (<c>+905321234567</c>). Numbers without a country code are read as Turkish:
/// <c>0532 123 45 67</c> and <c>532 123 45 67</c> both become <c>+905321234567</c>.
/// </summary>
public sealed record PhoneNumber
{
    public const string TurkeyCountryCode = "90";
    private const int TurkishNationalNumberLength = 10;
    private const int MinDigits = 8;
    private const int MaxDigits = 15;

    /// <summary>Longest stored value: <c>+</c> and 15 digits.</summary>
    public const int MaxLength = MaxDigits + 1;

    private PhoneNumber(string value) => Value = value;

    /// <summary>E.164 representation including the leading <c>+</c>.</summary>
    public string Value { get; }

    public bool IsTurkish => Value.StartsWith("+" + TurkeyCountryCode, StringComparison.Ordinal);

    public static Result<PhoneNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CommonErrors.InvalidPhoneNumber;
        }

        var raw = value.Trim();
        var international = raw.StartsWith('+');
        var digits = new StringBuilder(raw.Length);
        foreach (var c in raw.AsSpan(international ? 1 : 0))
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
            {
                return CommonErrors.InvalidPhoneNumber;
            }
        }

        var number = digits.ToString();
        if (!international)
        {
            if (number.StartsWith("00", StringComparison.Ordinal))
            {
                number = number[2..];
            }
            else if (number.StartsWith('0'))
            {
                number = TurkeyCountryCode + number[1..];
            }
            else if (number.Length == TurkishNationalNumberLength)
            {
                number = TurkeyCountryCode + number;
            }
            else
            {
                return CommonErrors.InvalidPhoneNumber;
            }
        }

        if (number.Length is < MinDigits or > MaxDigits || number[0] == '0')
        {
            return CommonErrors.InvalidPhoneNumber;
        }

        if (number.StartsWith(TurkeyCountryCode, StringComparison.Ordinal) && !IsValidTurkishNationalNumber(number[2..]))
        {
            return CommonErrors.InvalidPhoneNumber;
        }

        return new PhoneNumber("+" + number);
    }

    public override string ToString() => Value;

    // Geographic (2xx, 3xx, 4xx), mobile (5xx) and non-geographic (8xx) numbers have 10 digits.
    private static bool IsValidTurkishNationalNumber(string national) =>
        national.Length == TurkishNationalNumberLength && national[0] is '2' or '3' or '4' or '5' or '8';
}
