using System.Text.Json.Serialization;

namespace Merp.Application.Abstractions.Approvals;

/// <summary>Type of a fact a document type offers to approval conditions (docs/domain/yetki-ve-onay.md §2.1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalFactType>))]
public enum ApprovalFactType
{
    Text,
    Number,
    Boolean,
    Date,

    /// <summary>An id, e.g. of a user or an org unit.</summary>
    Identifier,

    /// <summary>A list of texts, e.g. the codes of the leave types of a request.</summary>
    TextList,
}

/// <param name="Name">Key in the facts JSON, e.g. <c>days</c>.</param>
/// <param name="Type">Value type; conditions are checked against it.</param>
/// <param name="LabelKey">Localization key of the name shown in the flow editor.</param>
public sealed record ApprovalFact(string Name, ApprovalFactType Type, string LabelKey);

/// <summary>
/// A document type that can be sent for approval (§2.1), e.g. <c>hr.leave-request</c>. The first segment is the
/// module. Without a matching definition the request is approved at once when
/// <paramref name="AutoApproveWhenNoDefinition"/> is set, otherwise it needs attention (§2.2).
/// </summary>
/// <param name="Key">Document type, e.g. <c>hr.leave-request</c>.</param>
/// <param name="NameKey">Localization key of the type's name.</param>
/// <param name="Facts">Facts the conditions may use.</param>
/// <param name="AutoApproveWhenNoDefinition">Approve requests no definition matches.</param>
/// <param name="DefaultFlow">
/// Steps of the general definition seeded in every tenant that has no definition of the type yet (§2.8, D34); null
/// seeds nothing.
/// </param>
public sealed record ApprovalDocumentType(
    string Key,
    string NameKey,
    IReadOnlyList<ApprovalFact> Facts,
    bool AutoApproveWhenNoDefinition = false,
    IReadOnlyList<ApprovalDefaultStep>? DefaultFlow = null);

/// <summary>
/// A step of a seeded flow. A <c>role</c> step names a system role by its code (<c>roleCode</c>, e.g.
/// <c>hr-specialist</c>); the seeder puts the tenant's role id in its place.
/// </summary>
/// <param name="NameKey">Localization key of the step's name; its text in the default culture is stored.</param>
/// <param name="Resolver">Resolver key, e.g. <c>line-manager</c>.</param>
/// <param name="Parameters">Resolver parameters, e.g. <c>level = 1</c>.</param>
/// <param name="Condition">JSON rule tree of §2.3; null always applies.</param>
public sealed record ApprovalDefaultStep(string NameKey, string Resolver, IReadOnlyDictionary<string, string> Parameters, string? Condition = null)
{
    /// <summary>Parameter of a seeded <c>role</c> step that names a system role.</summary>
    public const string RoleCodeParameter = "roleCode";
}

/// <summary>Modules declare their approvable document types, like their permissions.</summary>
public interface IApprovalDocumentTypeProvider
{
    IEnumerable<ApprovalDocumentType> GetDocumentTypes();
}

/// <summary>
/// Whether the caller may read a document of the module's types, so those who see the document in the module also
/// see its approval timeline (§2.7, D34). The module implements it with the document's own permissions and data
/// scope, as files follow their record (D31). Requesters, approvers and approval administrators see the timeline
/// without it.
/// </summary>
public interface IApprovalDocumentAccessPolicy
{
    /// <summary>Document types it decides for, e.g. <c>hr.leave-request</c>.</summary>
    IReadOnlyCollection<string> DocumentTypes { get; }

    Task<bool> CanReadAsync(string documentType, string documentId, CancellationToken cancellationToken = default);
}
