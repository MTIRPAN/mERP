using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.Platform.Contracts.Approvals;

/// <summary>What a resolver knows about the request whose approvers it finds.</summary>
public sealed record ApproverResolutionContext(
    string DocumentType,
    Guid? CompanyId,
    Guid RequesterUserId,
    Guid? RequesterEmployeeId,
    JsonElement Facts);

/// <summary>A parameter of a resolver, for the flow editor; values are passed as strings.</summary>
/// <param name="Name">Parameter name, e.g. <c>roleId</c>.</param>
/// <param name="LabelKey">Localization key shown in the editor.</param>
/// <param name="Required">The definition must give a value.</param>
public sealed record ApproverParameter(string Name, string LabelKey, bool Required = true);

/// <summary>
/// Finds the approvers of a step (§2.4). Platform registers <c>user</c>, <c>role</c> and <c>fact-user</c>; HR adds
/// <c>line-manager</c>, <c>org-unit-manager</c> and <c>position</c>. An empty result falls back to the definition's
/// fallback approver, then to "needs attention".
/// </summary>
public interface IApproverResolver
{
    /// <summary>Key used in definitions, e.g. <c>role</c>.</summary>
    string Key { get; }

    /// <summary>Localization key of the resolver's name in the flow editor.</summary>
    string NameKey { get; }

    IReadOnlyList<ApproverParameter> Parameters { get; }

    /// <summary>Active platform users who approve; empty when there is none.</summary>
    Task<IReadOnlyList<Guid>> ResolveAsync(
        ApproverResolutionContext context,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The manager of an approver, for the <c>escalate-to-manager</c> escalation (§2.5). HR implements it; without HR an
/// overdue task cannot move and its approvers are reminded instead.
/// </summary>
public interface IApproverManagerLookup
{
    Task<Guid?> FindManagerUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends a document for approval (§2.7). Publishing it again for a returned document resubmits it: the flow starts
/// again at the first step. IntegrationEvent.CompanyId is the document's company.
/// </summary>
/// <param name="DocumentType">A registered document type.</param>
/// <param name="DocumentId">The document's id in its module.</param>
/// <param name="RequesterUserId">Platform user who sends it.</param>
/// <param name="RequesterEmployeeId">The requester's employee, for HR resolvers.</param>
/// <param name="Facts">Values the conditions use, per the document type's facts.</param>
/// <param name="TitleKey">Localization key of the title in the approval inbox.</param>
/// <param name="TitleArgs">Placeholder values of the title.</param>
/// <param name="Summary">Short description, e.g. "7 gün yıllık izin".</param>
/// <param name="Link">Path of the document in the web application.</param>
public sealed record ApprovalRequested(
    string DocumentType,
    string DocumentId,
    Guid RequesterUserId,
    Guid? RequesterEmployeeId,
    JsonElement Facts,
    string TitleKey,
    IReadOnlyDictionary<string, string> TitleArgs,
    string? Summary,
    string? Link) : IntegrationEvent;

/// <summary>The document was withdrawn: Platform closes its open tasks (§2.7).</summary>
public sealed record ApprovalCancellationRequested(string DocumentType, string DocumentId) : IntegrationEvent;

[JsonConverter(typeof(JsonStringEnumConverter<ApprovalOutcome>))]
public enum ApprovalOutcome
{
    Approved,
    Rejected,
}

/// <summary>The flow ended; the module approves or rejects the document (§2.7).</summary>
/// <remarks>Reason says why it ended without people deciding, e.g. <c>no_definition</c> or <c>overdue</c>; null otherwise.</remarks>
public sealed record ApprovalRequestCompleted(
    string DocumentType,
    string DocumentId,
    ApprovalOutcome Outcome,
    DateTimeOffset DecidedAt,
    string? Reason) : IntegrationEvent;

/// <summary>An approver returned the document for correction; sending ApprovalRequested again resubmits it.</summary>
public sealed record ApprovalRequestReturned(
    string DocumentType,
    string DocumentId,
    Guid ReturnedByUserId,
    string Comment,
    DateTimeOffset ReturnedAt) : IntegrationEvent;

/// <summary>Approvers got a task (for modules that track it; Platform notifies the approvers itself).</summary>
public sealed record ApprovalTaskAssigned(
    string DocumentType,
    string DocumentId,
    Guid RequestId,
    int Step,
    IReadOnlyList<Guid> AssigneeUserIds) : IntegrationEvent;

/// <summary>
/// "Who will approve this?" before a document is sent (§2.7): the flow that would be chosen and the approvers of
/// each step, as the engine would assign them now. Shown on the module's form.
/// </summary>
public interface IApprovalPreview
{
    Task<ApprovalPreview> PreviewAsync(
        string documentType,
        Guid? companyId,
        JsonElement facts,
        Guid requesterUserId,
        Guid? requesterEmployeeId,
        CancellationToken cancellationToken = default);
}

/// <param name="HasDefinition">A flow matches the document.</param>
/// <param name="AutoApproved">No flow matches and the type approves such documents at once.</param>
/// <param name="Steps">The flow's steps in order; empty without a flow.</param>
public sealed record ApprovalPreview(bool HasDefinition, bool AutoApproved, IReadOnlyList<ApprovalPreviewStep> Steps);

/// <param name="Index">0-based step index.</param>
/// <param name="Name">Step name in the caller's language.</param>
/// <param name="Approvers">Who gets the task; empty when skipped or nobody was found.</param>
/// <param name="SkipReason"><c>condition</c>, <c>requester</c> or <c>already_approved</c>; null when the step applies.</param>
/// <param name="NeedsAttention">Nobody was found, not even a fallback: an administrator would have to assign.</param>
public sealed record ApprovalPreviewStep(int Index, string Name, IReadOnlyList<ApprovalPreviewApprover> Approvers, string? SkipReason, bool NeedsAttention);

public sealed record ApprovalPreviewApprover(Guid UserId, string Name);

/// <summary>
/// Task-based access (docs/domain/yetki-ve-onay.md §1, rule 5): a user with a pending or past task on a document, or
/// acting for its approver today, may see the document without the module's permission.
/// </summary>
public interface IApprovalAccess
{
    Task<bool> HasTaskAsync(Guid userId, string documentType, string documentId, CancellationToken cancellationToken = default);
}
