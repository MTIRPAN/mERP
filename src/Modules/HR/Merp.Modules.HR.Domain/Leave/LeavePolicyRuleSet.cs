using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

namespace Merp.Modules.HR.Domain.Leave;

public readonly record struct LeavePolicyRuleSetId(Guid Value) : IEntityId<LeavePolicyRuleSetId>
{
    public static LeavePolicyRuleSetId From(Guid value) => new(value);
}

/// <summary>Employees all of whose criteria match get <paramref name="PolicyId"/>; no criteria match everyone.</summary>
public sealed record PolicyAssignmentRule(string Name, LeavePolicyId PolicyId, IReadOnlyList<PolicyCriterion> Criteria)
{
    public const int NameMaxLength = 100;
    public const int MaxCriteria = 10;

    public bool Matches(PolicySubject subject) => Criteria.All(c => c.Matches(subject));
}

/// <summary>
/// The policy assignment rules of a company, in priority order (docs/domain/izin-kurallari.md §1, D41): the first rule
/// that matches an employee gives their policy. One list per company, so the order changes in one step.
/// </summary>
[Audited]
public sealed class LeavePolicyRuleSet : AuditableAggregateRoot<LeavePolicyRuleSetId>, ICompanyScoped
{
    public const int MaxRules = 100;

    private LeavePolicyRuleSet()
    {
    }

    public Guid CompanyId { get; private set; }

    public IReadOnlyList<PolicyAssignmentRule> Rules { get; private set; } = [];

    public static LeavePolicyRuleSet Create(Guid companyId) => companyId == Guid.Empty
        ? throw new ArgumentException("A rule set belongs to a company.", nameof(companyId))
        : new LeavePolicyRuleSet { Id = EntityId.New<LeavePolicyRuleSetId>(), CompanyId = companyId };

    public void Replace(IReadOnlyList<PolicyAssignmentRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = rules.Count <= MaxRules ? [.. rules] : throw new ArgumentException($"At most {MaxRules} rules.", nameof(rules));
    }

    /// <summary>The first rule that matches, with its position from 1; rules of policies <paramref name="usable"/> rejects are skipped.</summary>
    public (PolicyAssignmentRule Rule, int Position)? FirstMatch(PolicySubject subject, Func<LeavePolicyId, bool> usable)
    {
        ArgumentNullException.ThrowIfNull(usable);
        for (var i = 0; i < Rules.Count; i++)
        {
            if (usable(Rules[i].PolicyId) && Rules[i].Matches(subject))
            {
                return (Rules[i], i + 1);
            }
        }

        return null;
    }
}
