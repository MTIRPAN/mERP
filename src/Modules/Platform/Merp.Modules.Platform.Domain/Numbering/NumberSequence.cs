using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Numbering;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Domain.Numbering;

public readonly record struct NumberSequenceId(Guid Value) : IEntityId<NumberSequenceId>
{
    public static NumberSequenceId From(Guid value) => new(value);
}

/// <summary>
/// The number pattern and reset period of a document type (PLAN P5, D29): the tenant default (no company) or a
/// company's own. The counters live in the document type's module; this only says how numbers look.
/// </summary>
[Audited]
public sealed class NumberSequence : AuditableAggregateRoot<NumberSequenceId>
{
    public const int DocumentTypeMaxLength = 100;

    private NumberSequence()
    {
    }

    public string DocumentType { get; private set; } = null!;

    /// <summary>Null for the tenant default; otherwise the company the pattern applies to.</summary>
    public Guid? CompanyId { get; private set; }

    public string Pattern { get; private set; } = null!;

    public NumberResetPeriod ResetPeriod { get; private set; }

    public bool IsDefault => CompanyId is null;

    public static Result<NumberSequence> Create(string documentType, Guid? companyId, string pattern, NumberResetPeriod resetPeriod)
    {
        var sequence = new NumberSequence
        {
            Id = EntityId.New<NumberSequenceId>(),
            DocumentType = Text.Required(documentType, DocumentTypeMaxLength, nameof(documentType)),
            CompanyId = companyId,
        };
        var changed = sequence.Change(pattern, resetPeriod);
        return changed.IsSuccess ? sequence : changed.Error;
    }

    /// <summary>A new pattern applies to the next number; the counter of the period goes on.</summary>
    public Result Change(string pattern, NumberResetPeriod resetPeriod)
    {
        if (NumberPattern.Validate(pattern, resetPeriod) is { } code)
        {
            return Error.Validation([new FieldError("pattern", code)]);
        }

        Pattern = pattern;
        ResetPeriod = resetPeriod;
        return Result.Success();
    }
}
