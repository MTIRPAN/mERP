using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Merp.Application.Abstractions.Documents;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Merp.Infrastructure.Documents;

/// <summary>
/// PDF text through PdfPig, DOCX text through the Open XML SDK (D19). Uploaded files are untrusted: the format is
/// detected from the signature, and pages, characters and package part sizes are limited.
/// </summary>
internal sealed partial class DocumentTextExtractor(ILogger<DocumentTextExtractor> logger) : IDocumentTextExtractor
{
    public const int MaxPages = 30;
    public const int MaxCharacters = 100_000;

    // Guards against zip bombs: no part of a DOCX package may expand beyond this many characters.
    private const long MaxCharactersInPart = 20_000_000;

    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;

    private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

    public Result<ExtractedDocument> Extract(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var span = content.Span;
        try
        {
            if (span.StartsWith(PdfSignature))
            {
                return ExtractPdf(content, cancellationToken);
            }

            if (span.StartsWith(ZipSignature))
            {
                return ExtractDocx(content);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnreadable(logger, exception);
            return DocumentErrors.Unreadable;
        }

        return DocumentErrors.UnsupportedFormat;
    }

    private static Result<ExtractedDocument> ExtractPdf(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(content.ToArray());
        var builder = new StringBuilder();
        var pages = Math.Min(document.NumberOfPages, MaxPages);
        var truncated = document.NumberOfPages > MaxPages;
        for (var number = 1; number <= pages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine(ContentOrderTextExtractor.GetText(document.GetPage(number)));
            if (builder.Length > MaxCharacters)
            {
                truncated = true;
                break;
            }
        }

        return Complete(DocumentKind.Pdf, builder, pages, truncated);
    }

    private static Result<ExtractedDocument> ExtractDocx(ReadOnlyMemory<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false, new OpenSettings { MaxCharactersInPart = MaxCharactersInPart });
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return DocumentErrors.UnsupportedFormat;
        }

        // Headers and footers are skipped on purpose: they mostly hold contact details, which AI features do not need.
        var builder = new StringBuilder();
        var truncated = false;
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            builder.AppendLine(paragraph.InnerText);
            if (builder.Length > MaxCharacters)
            {
                truncated = true;
                break;
            }
        }

        return Complete(DocumentKind.Docx, builder, 1, truncated);
    }

    private static Result<ExtractedDocument> Complete(DocumentKind kind, StringBuilder builder, int pages, bool truncated)
    {
        var text = builder.Length > MaxCharacters ? builder.ToString(0, MaxCharacters) : builder.ToString();
        text = text.Trim();
        return text.Count(char.IsLetter) < 20
            ? DocumentErrors.NoText
            : new ExtractedDocument(kind, text, pages, truncated);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Uploaded document could not be read")]
    private static partial void LogUnreadable(ILogger logger, Exception exception);
}
