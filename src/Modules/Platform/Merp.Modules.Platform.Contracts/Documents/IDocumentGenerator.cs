using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Contracts.Documents;

/// <param name="DocumentType">A key a module declared with <c>IDocumentTypeProvider</c>, e.g. <c>hr.leave_form</c>.</param>
/// <param name="CompanyId">The company the document belongs to: its template, letterhead and logo are used.</param>
/// <param name="Culture">Language of the document, e.g. <c>tr-TR</c>; dates and numbers follow it.</param>
/// <param name="Model">Values of the document type's fields; nested dictionaries and lists are allowed.</param>
public sealed record DocumentRequest(string DocumentType, Guid CompanyId, string Culture, IReadOnlyDictionary<string, object?> Model);

/// <param name="Content">The PDF.</param>
/// <param name="TemplateId">The tenant's template used, or null for the built-in one.</param>
/// <param name="Revision">Revision of the template used; 0 for the built-in one.</param>
public sealed record GeneratedDocument(byte[] Content, Guid? TemplateId, int Revision);

/// <summary>
/// Prints a document with the tenant's template (D36): the company's template in the culture, otherwise the one for
/// all companies, otherwise the built-in one. Read only: a module that keeps the PDF stores it itself (D31).
/// </summary>
public interface IDocumentGenerator
{
    /// <summary>Fails with a validation error when the template cannot render the model, or when PDF conversion is unavailable.</summary>
    Task<Result<GeneratedDocument>> GetPdfAsync(DocumentRequest request, CancellationToken cancellationToken = default);
}
