using System.IO.Compression;
using System.Text;

namespace Merp.Testing.Data;

/// <summary>
/// Smallest files that pass the content checks of the file store (PLAN P7), and a few that must not. Shared by the
/// unit tests (linked source) and the integration tests.
/// </summary>
public static class SampleFiles
{
    public static byte[] Pdf() => "%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    /// <summary>A 1×1 transparent PNG.</summary>
    public static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    public static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];

    public static byte[] Webp() => [.. "RIFF"u8, 0x1A, 0x00, 0x00, 0x00, .. "WEBPVP8L"u8, 0x0D, 0x00, 0x00, 0x00, 0x2F, 0x00, 0x00, 0x00, 0x10, 0x07, 0x10, 0x11, 0x11, 0x88, 0x88, 0xFE, 0x07, 0x00];

    /// <summary>A Windows program header, as in an executable renamed to <c>.pdf</c>.</summary>
    public static byte[] Executable() => [.. "MZ"u8, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];

    public static byte[] Docx(bool withMacros = false) =>
        Package(withMacros ? ["word/document.xml", "word/vbaProject.bin"] : ["word/document.xml"]);

    public static byte[] Xlsx() => Package(["xl/workbook.xml"]);

    public static byte[] Text(string text = "Ad;Şehir\nAyşe;İstanbul\n") => Encoding.UTF8.GetBytes(text);

    /// <summary>An Office Open XML package with the content types part and <paramref name="parts"/>.</summary>
    private static byte[] Package(string[] parts)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in parts.Prepend("[Content_Types].xml"))
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><x/>");
            }
        }

        return stream.ToArray();
    }
}
