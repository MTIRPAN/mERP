using System.Text;
using Merp.Infrastructure.Files;
using Merp.Testing.Data;

namespace Merp.BuildingBlocks.UnitTests.Files;

public sealed class FileContentInspectorTests
{
    public static TheoryData<string, byte[], string> Valid => new()
    {
        { "pdf", SampleFiles.Pdf(), "application/pdf" },
        { "png", SampleFiles.Png(), "image/png" },
        { "jpg", SampleFiles.Jpeg(), "image/jpeg" },
        { "jpeg", SampleFiles.Jpeg(), "image/jpeg" },
        { "webp", SampleFiles.Webp(), "image/webp" },
        { "docx", SampleFiles.Docx(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "xlsx", SampleFiles.Xlsx(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { "txt", SampleFiles.Text(), "text/plain; charset=utf-8" },
        { "csv", SampleFiles.Text(), "text/csv; charset=utf-8" },
    };

    public static TheoryData<string, byte[]> Mismatched => new()
    {
        // A program renamed to a document or a picture
        { "pdf", SampleFiles.Executable() },
        { "png", SampleFiles.Executable() },
        // One kind renamed to another
        { "png", SampleFiles.Jpeg() },
        { "pdf", SampleFiles.Png() },
        { "docx", SampleFiles.Xlsx() },
        { "xlsx", SampleFiles.Docx() },
        // A macro document renamed to .docx
        { "docx", SampleFiles.Docx(withMacros: true) },
        // A zip that is not an Office document, and a truncated header
        { "docx", [0x50, 0x4B, 0x03, 0x04, 0x00] },
        { "pdf", "%PD"u8.ToArray() },
        // Binary data or a legacy code page as text
        { "txt", [0x41, 0x00, 0x42] },
        { "csv", [0x41, 0x3B, 0xDD, 0x0A] },
        { "txt", SampleFiles.Png() },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void MatchingContent_ReturnsTheServersMediaType(string extension, byte[] content, string contentType) =>
        FileContentInspector.Inspect(extension, new MemoryStream(content)).ShouldBe(contentType);

    [Theory]
    [MemberData(nameof(Mismatched))]
    public void ContentThatIsNotWhatTheExtensionSays_IsRefused(string extension, byte[] content) =>
        FileContentInspector.Inspect(extension, new MemoryStream(content)).ShouldBeNull();

    [Theory]
    [InlineData("exe")]
    [InlineData("svg")]
    [InlineData("html")]
    [InlineData("zip")]
    [InlineData("doc")]
    public void ExtensionsWithoutAContentCheck_AreNeverAccepted(string extension)
    {
        FileContentInspector.KnownExtensions.ShouldNotContain(extension);
        FileContentInspector.Inspect(extension, new MemoryStream(SampleFiles.Pdf())).ShouldBeNull();
    }

    [Fact]
    public void Text_WithAByteOrderMarkAndTurkishLetters_IsAccepted()
    {
        var content = Encoding.UTF8.GetPreamble().Concat(SampleFiles.Text("Çağrı;Işık;Göğüş\r\n\tÜç")).ToArray();

        FileContentInspector.Inspect("txt", new MemoryStream(content)).ShouldNotBeNull();
    }
}
