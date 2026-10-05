using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Merp.Application.Abstractions.Documents;
using Merp.Infrastructure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PageSize = UglyToad.PdfPig.Content.PageSize;

namespace Merp.BuildingBlocks.UnitTests.Documents;

public sealed class DocumentTextExtractorTests
{
    private readonly DocumentTextExtractor _extractor = new(NullLogger<DocumentTextExtractor>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Pdf_ReturnsTextInReadingOrder()
    {
        var pdf = Pdf("Senior software engineer", "Eight years of .NET experience");

        var result = _extractor.Extract(pdf, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe(DocumentKind.Pdf);
        result.Value.PageCount.ShouldBe(1);
        result.Value.Text.ShouldContain("Senior software engineer");
        result.Value.Text.ShouldContain("Eight years of .NET experience");
        result.Value.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void Docx_ReturnsBodyParagraphs_WithTurkishText()
    {
        var docx = Docx("Kıdemli yazılım mühendisi", "İstanbul'da 8 yıl deneyim, ölçeklenebilir sistemler");

        var result = _extractor.Extract(docx, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe(DocumentKind.Docx);
        result.Value.Text.ShouldBe("Kıdemli yazılım mühendisi\nİstanbul'da 8 yıl deneyim, ölçeklenebilir sistemler");
    }

    [Fact]
    public void PdfWithoutTextLayer_FailsWithNoText() =>
        _extractor.Extract(Pdf(), Ct).Error.ShouldBe(DocumentErrors.NoText);

    [Fact]
    public void UnknownFormat_IsRejected() =>
        _extractor.Extract("Plain text pretending to be a CV"u8.ToArray(), Ct).Error.ShouldBe(DocumentErrors.UnsupportedFormat);

    [Fact]
    public void CorruptPdf_IsUnreadable() =>
        _extractor.Extract("%PDF-1.7 this is not really a pdf"u8.ToArray(), Ct).Error.ShouldBe(DocumentErrors.Unreadable);

    [Fact]
    public void ZipThatIsNotADocx_IsUnreadable() =>
        _extractor.Extract(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02 }, Ct).Error.ShouldBe(DocumentErrors.Unreadable);

    private static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < lines.Length; i++)
        {
            page.AddText(lines[i], 12, new PdfPoint(40, 780 - (i * 20)), font);
        }

        return builder.Build();
    }

    private static byte[] Docx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs.Select(p => new Paragraph(new Run(new Text(p))))));
        }

        return stream.ToArray();
    }
}
