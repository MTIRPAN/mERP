using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Documents;

/// <summary>
/// A kind of document a module prints, e.g. <c>hr.leave_form</c> (D36). The built-in template is used until the tenant
/// defines its own; the sample model renders previews and checks templates when they are saved.
/// </summary>
/// <param name="Key">Stable code, <c>{module}.{name}</c> in lower case.</param>
/// <param name="NameKey">Localization key of the name shown in lists.</param>
/// <param name="DefaultBody">Built-in Liquid HTML.</param>
/// <param name="DefaultCss">Built-in style sheet.</param>
/// <param name="SampleModel">Values a real document gets, made up; <c>company</c> and <c>document</c> are added by Platform.</param>
/// <param name="Fields">Variables a template can use, as help in the editor.</param>
public sealed record DocumentTypeDefinition(
    string Key,
    string NameKey,
    string DefaultBody,
    string DefaultCss,
    IReadOnlyDictionary<string, object?> SampleModel,
    IReadOnlyList<DocumentField> Fields);

/// <param name="Path">Liquid path, e.g. <c>recipient.name</c>.</param>
/// <param name="DescriptionKey">Localization key of what the value is.</param>
public sealed record DocumentField(string Path, string DescriptionKey);

/// <summary>Modules declare their document types, as they declare permissions and notification types.</summary>
public interface IDocumentTypeProvider
{
    IEnumerable<DocumentTypeDefinition> GetDocumentTypes();
}

/// <summary>Turns an HTML page into a PDF (D36). The page must be self-contained: nothing is downloaded.</summary>
public interface IPdfConverter
{
    /// <summary>A4, backgrounds printed. Fails with <see cref="PdfErrors.Unavailable"/> when the converter cannot be reached.</summary>
    Task<Result<byte[]>> ConvertHtmlAsync(string html, CancellationToken cancellationToken);
}

public static class PdfErrors
{
    public static readonly Error Unavailable = Error.Unavailable("common.errors.pdf_unavailable");
}
