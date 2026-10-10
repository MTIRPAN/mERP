using Merp.Application.Abstractions.Files;
using Merp.Infrastructure.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Merp.Web.Files;

/// <summary>Upload and download helpers for module file endpoints (PLAN P7, D31).</summary>
public static class FileTransfer
{
    /// <summary>Room for the multipart envelope and the other form fields.</summary>
    private const long FormOverheadBytes = 64 * 1024;

    /// <summary>Request size limit of an attachment upload: <c>Files:MaxBytes</c> and the form envelope.</summary>
    public static long AttachmentRequestLimit(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.ServiceProvider.GetRequiredService<IOptions<FileStorageOptions>>().Value.MaxBytes + FormOverheadBytes;
    }

    /// <summary>Request size limit of an upload of at most <paramref name="maxBytes"/>.</summary>
    public static long RequestLimit(long maxBytes) => maxBytes + FormOverheadBytes;

    /// <summary>The form file as an upload; ASP.NET buffers form files, so the stream is seekable.</summary>
    public static FileUpload? ToUpload(this IFormFile? file) =>
        file is null ? null : new FileUpload(file.FileName, file.OpenReadStream());

    /// <summary>
    /// Sends stored content. Images may be shown inline (logos); everything else downloads. The browser must not
    /// guess the type or run anything in it.
    /// </summary>
    public static IResult Content(HttpContext context, string fileName, string contentType, long length, Stream content)
    {
        ArgumentNullException.ThrowIfNull(context);
        var headers = context.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; sandbox";
        headers[HeaderNames.CacheControl] = "private, no-store";
        context.Response.ContentLength = length;

        var inline = contentType.StartsWith("image/", StringComparison.Ordinal);
        return TypedResults.Stream(content, contentType, inline ? null : fileName);
    }
}
