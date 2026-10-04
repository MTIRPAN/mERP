using System.Text.RegularExpressions;
using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>E-mail address, trimmed and stored in lower case.</summary>
public sealed partial record Email
{
    public const int MaxLength = 254;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxLength || !Pattern().IsMatch(normalized))
        {
            return CommonErrors.InvalidEmail;
        }

        return new Email(normalized);
    }

    public override string ToString() => Value;

    // local@domain.tld: no whitespace, a single @, at least one dot in the domain, no leading/trailing dot or hyphen in labels
    [GeneratedRegex(@"^[^\s@]+@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
