using Merp.SharedKernel.Results;

namespace Merp.SharedKernel.ValueObjects;

/// <summary>
/// Social security workplace registration number (SGK işyeri sicil no): 26 digits. Spaces, dashes and dots used
/// in printed forms are ignored.
/// </summary>
public sealed record SgkWorkplaceNo
{
    public const int Length = 26;

    private SgkWorkplaceNo(string value) => Value = value;

    public string Value { get; }

    public static Result<SgkWorkplaceNo> Create(string? value)
    {
        var digits = MersisNo.DigitsOnly(value);
        return digits is { Length: Length } ? new SgkWorkplaceNo(digits) : CommonErrors.InvalidSgkWorkplaceNo;
    }

    public override string ToString() => Value;
}
