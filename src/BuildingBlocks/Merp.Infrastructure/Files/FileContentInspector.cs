using System.IO.Compression;
using System.Text;

namespace Merp.Infrastructure.Files;

/// <summary>
/// Checks that a file's bytes are what its extension claims (PLAN P7: content sniffing), so a program renamed to
/// <c>.pdf</c> or a macro document renamed to <c>.docx</c> is rejected. Only extensions listed here can be allowed;
/// the media type returned is the one the server sends on download.
/// </summary>
internal static class FileContentInspector
{
    private const string WordprocessingDocument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Spreadsheet = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Presentation = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    private static readonly Dictionary<string, Kind> Kinds = new(StringComparer.Ordinal)
    {
        ["pdf"] = new("application/pdf", Check.Pdf),
        ["png"] = new("image/png", Check.Png),
        ["jpg"] = new("image/jpeg", Check.Jpeg),
        ["jpeg"] = new("image/jpeg", Check.Jpeg),
        ["webp"] = new("image/webp", Check.Webp),
        ["docx"] = new(WordprocessingDocument, Check.OfficeOpenXml, "word/document.xml"),
        ["xlsx"] = new(Spreadsheet, Check.OfficeOpenXml, "xl/workbook.xml"),
        ["pptx"] = new(Presentation, Check.OfficeOpenXml, "ppt/presentation.xml"),
        ["txt"] = new("text/plain; charset=utf-8", Check.Utf8Text),
        ["csv"] = new("text/csv; charset=utf-8", Check.Utf8Text),
    };

    private enum Check
    {
        Pdf,
        Png,
        Jpeg,
        Webp,
        OfficeOpenXml,
        Utf8Text,
    }

    /// <summary>Extensions whose content can be checked; configuration may only allow these.</summary>
    public static IReadOnlyCollection<string> KnownExtensions => Kinds.Keys;

    /// <summary>
    /// The media type when <paramref name="content"/> matches <paramref name="extension"/>, otherwise null. The
    /// stream is read from the start and left at an unspecified position.
    /// </summary>
    public static string? Inspect(string extension, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Kinds.TryGetValue(extension, out var kind))
        {
            return null;
        }

        content.Position = 0;
        var matches = kind.Check switch
        {
            Check.Pdf => StartsWith(content, Pdf),
            Check.Png => StartsWith(content, Png),
            Check.Jpeg => StartsWith(content, Jpeg),
            Check.Webp => IsWebp(content),
            Check.OfficeOpenXml => IsOfficeOpenXml(content, kind.MainPart!),
            Check.Utf8Text => IsUtf8Text(content),
            _ => false,
        };
        return matches ? kind.ContentType : null;
    }

    private static bool StartsWith(Stream content, ReadOnlySpan<byte> signature)
    {
        Span<byte> header = stackalloc byte[signature.Length];
        return content.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length && header.SequenceEqual(signature);
    }

    // RIFF <size> WEBP
    private static bool IsWebp(Stream content)
    {
        Span<byte> header = stackalloc byte[12];
        return content.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
            && header[..4].SequenceEqual(Riff)
            && header[8..].SequenceEqual(Webp);
    }

    /// <summary>
    /// A zip package with the content types part and the main part of the format. Only the central directory is
    /// read, nothing is decompressed. Packages with VBA projects (macro documents renamed) are refused.
    /// </summary>
    private static bool IsOfficeOpenXml(Stream content, string mainPart)
    {
        if (!StartsWith(content, Zip))
        {
            return false;
        }

        content.Position = 0;
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return names.Contains("[Content_Types].xml")
                && names.Contains(mainPart)
                && !names.Any(name => name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>UTF-8 (BOM optional) without control characters other than tab, line breaks and form feed.</summary>
    private static bool IsUtf8Text(Stream content)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        using var reader = new StreamReader(content, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
        var buffer = new char[16 * 1024];
        try
        {
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                foreach (var c in buffer.AsSpan(0, read))
                {
                    if (char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\f'))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private sealed record Kind(string ContentType, Check Check, string? MainPart = null);
}
