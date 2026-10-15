using Merp.Application.Abstractions.Documents;
using Merp.Modules.Platform.Contracts.Documents;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Application.Documents;

public static class DocumentErrors
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.documents.errors.tenant_scope_required");

    public const string UnknownTypeCode = "platform.documents.errors.unknown_type";
}

/// <summary>Documents for other modules (D36): the composed page converted to PDF; nothing is stored.</summary>
public sealed class DocumentGenerator(DocumentTypeRegistry types, DocumentComposer composer, IPdfConverter pdf) : IDocumentGenerator
{
    public async Task<Result<GeneratedDocument>> RenderPdfAsync(DocumentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A module asking for a type nobody declared is a programming error, not a user's
        var type = types.Find(request.DocumentType) ?? throw new ArgumentException($"Unknown document type {request.DocumentType}.", nameof(request));
        var page = await composer.ComposeAsync(type, request.CompanyId, request.Culture, request.Model, content: null, cancellationToken);
        if (page.IsFailure)
        {
            return page.Error;
        }

        var content = await pdf.ConvertHtmlAsync(page.Value.Html, cancellationToken);
        return content.IsSuccess ? new GeneratedDocument(content.Value, page.Value.TemplateId, page.Value.Revision) : content.Error;
    }
}
