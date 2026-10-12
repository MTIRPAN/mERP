using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Application.Approvals;

/// <summary>Settings of the approval engine; the host binds them from <c>Platform:Approvals</c>.</summary>
public sealed class ApprovalSettings
{
    public const string SectionName = "Platform:Approvals";

    /// <summary>
    /// Registers the <c>platform.approval-test</c> document type, so flow administrators can send test documents
    /// through a flow from the flow screen (D34). On in development; a production tenant does not need it.
    /// </summary>
    public bool EnableTestDocuments { get; set; }
}

/// <summary>The test document type: a module-less document to try flows end to end (PLAN P10 acceptance).</summary>
public sealed class ApprovalTestDocumentTypes : IApprovalDocumentTypeProvider
{
    public const string Key = "platform.approval-test";
    public const string TitleKey = "platform.approvals.test_document.title";

    public IEnumerable<ApprovalDocumentType> GetDocumentTypes() =>
    [
        new(
            Key,
            "platform.approvals.test_document.name",
            [
                new("amount", ApprovalFactType.Number, "platform.approvals.test_document.amount"),
                new("category", ApprovalFactType.Text, "platform.approvals.test_document.category"),
                new("approverUserId", ApprovalFactType.Identifier, "platform.approvals.test_document.approver"),
            ]),
    ];
}

/// <summary>
/// Sends a test document for approval as the caller, the way a module does: through <see cref="ApprovalRequested"/>
/// in the outbox. 404 unless test documents are enabled. Returns the document id.
/// </summary>
public sealed record SubmitTestApprovalCommand(Guid? CompanyId, string? Summary, decimal? Amount, string? Category, Guid? ApproverUserId)
    : ICommand<string>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.ApprovalDefinitions.Manage;
}

internal sealed class SubmitTestApprovalCommandValidator : AbstractValidator<SubmitTestApprovalCommand>
{
    public SubmitTestApprovalCommandValidator()
    {
        RuleFor(c => c.Summary).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Category).MaximumLength(50);
    }
}

internal sealed class SubmitTestApprovalCommandHandler(
    ApprovalSettings settings,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher) : ICommandHandler<SubmitTestApprovalCommand, string>
{
    public async Task<Result<string>> Handle(SubmitTestApprovalCommand request, CancellationToken cancellationToken)
    {
        if (!settings.EnableTestDocuments)
        {
            return CommonErrors.NotFound;
        }

        var documentId = Guid.CreateVersion7().ToString();
        var facts = new Dictionary<string, object?>
        {
            ["amount"] = request.Amount,
            ["category"] = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            ["approverUserId"] = request.ApproverUserId,
        };
        await publisher.PublishAsync(
            new ApprovalRequested(
                ApprovalTestDocumentTypes.Key,
                documentId,
                currentUser.UserId!.Value,
                currentUser.EmployeeId,
                JsonSerializer.SerializeToElement(facts),
                ApprovalTestDocumentTypes.TitleKey,
                new Dictionary<string, string> { ["number"] = documentId[^6..].ToUpperInvariant() },
                request.Summary!.Trim(),
                null)
            {
                CompanyId = request.CompanyId,
            },
            cancellationToken);
        return documentId;
    }
}
