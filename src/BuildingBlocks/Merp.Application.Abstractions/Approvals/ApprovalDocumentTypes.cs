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
public sealed record ApprovalDocumentType(string Key, string NameKey, IReadOnlyList<ApprovalFact> Facts, bool AutoApproveWhenNoDefinition = false);

/// <summary>Modules declare their approvable document types, like their permissions.</summary>
public interface IApprovalDocumentTypeProvider
{
    IEnumerable<ApprovalDocumentType> GetDocumentTypes();
}
