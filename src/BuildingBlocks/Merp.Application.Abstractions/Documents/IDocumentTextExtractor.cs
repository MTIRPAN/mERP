using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Documents;

/// <summary>Extracts plain text from uploaded documents (PDF, DOCX) for search and AI features.</summary>
public interface IDocumentTextExtractor
{
    /// <summary>
    /// Reads <paramref name="content"/>. The format is detected from the file signature, not the name or the
    /// declared content type. Scanned PDFs without a text layer fail with <see cref="DocumentErrors.NoText"/>.
    /// </summary>
    Result<ExtractedDocument> Extract(ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

public enum DocumentKind
{
    Pdf,
    Docx,
}

/// <param name="Kind">Detected format.</param>
/// <param name="Text">Text in reading order, one paragraph or line per line.</param>
/// <param name="PageCount">Pages read (PDF); 1 for DOCX.</param>
/// <param name="Truncated">True when the document exceeded the page or character limit and only its start was read.</param>
public sealed record ExtractedDocument(DocumentKind Kind, string Text, int PageCount, bool Truncated);

public static class DocumentErrors
{
    public static readonly Error UnsupportedFormat = Error.Validation("common.errors.document_unsupported_format");
    public static readonly Error Unreadable = Error.Validation("common.errors.document_unreadable");
    public static readonly Error NoText = Error.Validation("common.errors.document_no_text");
}
