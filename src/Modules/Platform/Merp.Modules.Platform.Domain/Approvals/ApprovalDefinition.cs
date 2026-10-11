using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Approvals;

public readonly record struct ApprovalDefinitionId(Guid Value) : IEntityId<ApprovalDefinitionId>
{
    public static ApprovalDefinitionId From(Guid value) => new(value);
}

/// <summary>Who approves: a resolver key (<c>role</c>, <c>line-manager</c>...) and its parameters (§2.4).</summary>
public sealed record ApproverRule(string Resolver, IReadOnlyDictionary<string, string> Parameters);

/// <summary>Whether one approval or every approval completes a step (§2.5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalMode>))]
public enum ApprovalMode
{
    Any,
    All,
}

/// <summary>What happens to a task left open past its due time (§2.5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalEscalation>))]
public enum ApprovalEscalation
{
    None,
    Notify,
    EscalateToManager,
    AutoApprove,
    AutoReject,
}

/// <summary>
/// A step of a flow (§2.1, §2.5). Condition is the JSON rule tree of §2.3, checked against the document type's facts
/// by the application. Steps are copied into each request, so later changes never touch running requests.
/// </summary>
public sealed record ApprovalStep(
    IReadOnlyDictionary<string, string> Name,
    ApproverRule Approver,
    ApprovalMode Mode = ApprovalMode.Any,
    string? Condition = null,
    int? DueHours = null,
    ApprovalEscalation Escalation = ApprovalEscalation.None,
    bool SkipIfApproverIsRequester = true,
    bool SkipIfAlreadyApproved = true,
    bool AllowReturn = false);

/// <summary>
/// An approval flow of a document type (§2.1, §2.2): for every company or one, chosen by priority and entry
/// condition. Version counts the changes; requests record the version they copied.
/// </summary>
[Audited]
public sealed class ApprovalDefinition : AuditableAggregateRoot<ApprovalDefinitionId>
{
    public const int DocumentTypeMaxLength = 100;
    public const int MaxSteps = 20;

    private ApprovalDefinition()
    {
    }

    public string DocumentType { get; private set; } = null!;

    /// <summary>Null for every company; a company's own definition wins over the general ones.</summary>
    public Guid? CompanyId { get; private set; }

    public LocalizedString Name { get; private set; } = null!;

    /// <summary>Lower runs first among definitions of the same level.</summary>
    public int Priority { get; private set; }

    /// <summary>JSON rule tree; null always matches.</summary>
    public string? EntryCondition { get; private set; }

    public IReadOnlyList<ApprovalStep> Steps { get; private set; } = [];

    /// <summary>Approvers when a step's resolver finds nobody; null leads to "needs attention".</summary>
    public ApproverRule? Fallback { get; private set; }

    public bool IsActive { get; private set; }

    public int Revision { get; private set; }

    public static ApprovalDefinition Create(
        string documentType,
        Guid? companyId,
        LocalizedString name,
        int priority,
        string? entryCondition,
        IReadOnlyList<ApprovalStep> steps,
        ApproverRule? fallback)
    {
        var definition = new ApprovalDefinition
        {
            Id = EntityId.New<ApprovalDefinitionId>(),
            DocumentType = Text.Required(documentType, DocumentTypeMaxLength, nameof(documentType)),
            CompanyId = companyId,
            IsActive = true,
        };
        definition.Change(name, priority, entryCondition, steps, fallback, isActive: true);
        return definition;
    }

    /// <summary>Changes the flow; running requests keep the steps they copied (§2.2, scenario 8).</summary>
    public void Change(LocalizedString name, int priority, string? entryCondition, IReadOnlyList<ApprovalStep> steps, ApproverRule? fallback, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count is 0 or > MaxSteps)
        {
            throw new ArgumentException($"A flow has 1 to {MaxSteps} steps.", nameof(steps));
        }

        Name = name;
        Priority = priority;
        EntryCondition = string.IsNullOrWhiteSpace(entryCondition) ? null : entryCondition;
        Steps = [.. steps];
        Fallback = fallback;
        IsActive = isActive;
        Revision++;
    }
}
