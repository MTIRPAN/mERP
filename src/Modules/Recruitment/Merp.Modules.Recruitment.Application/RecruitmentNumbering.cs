using Merp.Application.Abstractions.Numbering;
using Merp.SharedKernel.Numbering;

namespace Merp.Modules.Recruitment.Application;

/// <summary>Numbered documents of the Recruitment module (PLAN P5); used by R2 (requisitions) and R6 (offers).</summary>
public static class RecruitmentDocumentTypes
{
    public const string Requisition = "recruitment.requisition";
    public const string Offer = "recruitment.offer";
}

public sealed class RecruitmentNumberSequences : INumberSequenceDefinitionProvider
{
    public IEnumerable<NumberSequenceDefinition> GetSequences() =>
    [
        new(RecruitmentDocumentTypes.Requisition, "PT-{yyyy}-{seq:5}", NumberResetPeriod.Yearly),
        new(RecruitmentDocumentTypes.Offer, "TKL-{yyyy}-{seq:5}", NumberResetPeriod.Yearly),
    ];
}
