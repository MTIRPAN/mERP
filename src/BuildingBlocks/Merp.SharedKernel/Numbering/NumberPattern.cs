using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Merp.SharedKernel.Numbering;

/// <summary>When the counter of a document type starts again from 1 (D29).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NumberResetPeriod>))]
public enum NumberResetPeriod
{
    Never,
    Yearly,
    Monthly,
}

/// <summary>
/// Number patterns: literal text with <c>{yyyy}</c>, <c>{yy}</c>, <c>{MM}</c> and exactly one <c>{seq}</c> or
/// <c>{seq:N}</c> (zero padded to N digits, 1–12). A yearly reset needs the year in the number, a monthly reset the
/// year and the month, otherwise numbers would repeat.
/// </summary>
public static partial class NumberPattern
{
    public const int MaxLength = 50;
    public const string InvalidCode = "common.errors.invalid_number_pattern";
    public const string PeriodMissingCode = "common.errors.number_pattern_period";

    public static bool HasYear(string pattern) => pattern.Contains("{yyyy}", StringComparison.Ordinal) || pattern.Contains("{yy}", StringComparison.Ordinal);

    /// <summary>The error code of an unusable pattern, or null when it is valid.</summary>
    public static string? Validate(string? pattern, NumberResetPeriod resetPeriod)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > MaxLength)
        {
            return InvalidCode;
        }

        var sequences = 0;
        foreach (Match token in Token().Matches(pattern))
        {
            var name = token.Groups["name"].Value;
            if (name == "seq")
            {
                sequences++;
                if (token.Groups["width"].Success && int.Parse(token.Groups["width"].Value, CultureInfo.InvariantCulture) is < 1 or > 12)
                {
                    return InvalidCode;
                }
            }
            else if (name is not ("yyyy" or "yy" or "MM") || token.Groups["width"].Success)
            {
                return InvalidCode;
            }
        }

        // Braces that are not tokens, or a sequence missing or repeated
        if (sequences != 1 || Token().Replace(pattern, string.Empty).IndexOfAny(['{', '}']) >= 0)
        {
            return InvalidCode;
        }

        return resetPeriod switch
        {
            NumberResetPeriod.Yearly when !HasYear(pattern) => PeriodMissingCode,
            NumberResetPeriod.Monthly when !HasYear(pattern) || !pattern.Contains("{MM}", StringComparison.Ordinal) => PeriodMissingCode,
            _ => null,
        };
    }

    /// <summary>The counter's period for a date: one counter for ever, per year or per month.</summary>
    public static string PeriodKey(NumberResetPeriod resetPeriod, DateOnly date) => resetPeriod switch
    {
        NumberResetPeriod.Yearly => date.ToString("yyyy", CultureInfo.InvariantCulture),
        NumberResetPeriod.Monthly => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => "-",
    };

    /// <summary>The number for a valid pattern.</summary>
    public static string Format(string pattern, DateOnly date, long value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var builder = new StringBuilder(pattern.Length + 8);
        var position = 0;
        foreach (Match token in Token().Matches(pattern))
        {
            builder.Append(pattern, position, token.Index - position);
            builder.Append(token.Groups["name"].Value switch
            {
                "yyyy" => date.ToString("yyyy", CultureInfo.InvariantCulture),
                "yy" => date.ToString("yy", CultureInfo.InvariantCulture),
                "MM" => date.ToString("MM", CultureInfo.InvariantCulture),
                _ => token.Groups["width"].Success
                    ? value.ToString(CultureInfo.InvariantCulture).PadLeft(int.Parse(token.Groups["width"].Value, CultureInfo.InvariantCulture), '0')
                    : value.ToString(CultureInfo.InvariantCulture),
            });
            position = token.Index + token.Length;
        }

        builder.Append(pattern, position, pattern.Length - position);
        return builder.ToString();
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)(?::(?<width>\d+))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
