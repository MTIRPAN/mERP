using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.Domain.Settings;

public readonly record struct RecruitmentLookupId(Guid Value) : IEntityId<RecruitmentLookupId>
{
    public static RecruitmentLookupId From(Guid value) => new(value);
}

/// <summary>The lists of choices the tenant keeps for recruitment (PLAN R1, D47).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LookupKind>))]
public enum LookupKind
{
    /// <summary>Why the company rejected a candidate.</summary>
    RejectionReason,

    /// <summary>Why a candidate withdrew.</summary>
    WithdrawalReason,

    /// <summary>Where a candidate came from (career page, LinkedIn, referral…).</summary>
    CandidateSource,

    /// <summary>A label HR puts on candidates.</summary>
    Tag,
}

/// <summary>
/// A choice of a recruitment list (red nedeni, aday kaynağı, etiket; PLAN R1). The code never changes and is unique
/// within its list; a retired choice stays on the records that use it.
/// </summary>
[Audited]
public sealed class RecruitmentLookup : AuditableAggregateRoot<RecruitmentLookupId>
{
    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 100;

    private RecruitmentLookup()
    {
    }

    public LookupKind Kind { get; private set; }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public static RecruitmentLookup Create(LookupKind kind, Code code, LocalizedString name, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown list.");
        }

        return new RecruitmentLookup
        {
            Id = EntityId.New<RecruitmentLookupId>(),
            Kind = kind,
            Code = code.Value,
            Name = name ?? throw new ArgumentNullException(nameof(name)),
            SortOrder = sortOrder,
            IsActive = true,
        };
    }

    public void Update(LocalizedString name, int sortOrder, bool isActive)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
