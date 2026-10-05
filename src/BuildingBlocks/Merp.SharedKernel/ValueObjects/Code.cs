using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Short business code of master data (company, branch, leave type...). Stored in upper case; only ASCII letters,
/// digits, <c>-</c>, <c>_</c> and <c>.</c> are allowed, so codes stay stable across cultures (Turkish İ/ı).
/// </summary>
public sealed record Code
{
    public const int DefaultMaxLength = 20;

    private Code(string value) => Value = value;

    public string Value { get; }

    public static Result<Code> Create(string? value, int maxLength = DefaultMaxLength)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return CommonErrors.Required;
        }

        if (normalized.Length > maxLength)
        {
            return CommonErrors.MaxLength(maxLength);
        }

        return normalized.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '-' or '_' or '.')
            ? new Code(normalized)
            : CommonErrors.InvalidCode;
    }

    public override string ToString() => Value;
}
