using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Merp.Application.Abstractions.Documents;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Documents;

/// <summary>PDF settings; the host binds them from the <c>Pdf</c> section (D36).</summary>
public sealed class PdfOptions
{
    public const string SectionName = "Pdf";

    /// <summary>Gotenberg 8, e.g. <c>http://localhost:3000</c> in development.</summary>
    public string GotenbergUrl { get; set; } = "http://localhost:3000";

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// HTML to PDF with Gotenberg's Chromium route (D14, D36): the page is sent as <c>index.html</c>, A4 with backgrounds.
/// The container denies every URL but its own working files and <c>data:</c> URIs, so a page cannot fetch anything.
/// </summary>
internal sealed partial class GotenbergPdfConverter(HttpClient http, IOptions<PdfOptions> options, ILogger<GotenbergPdfConverter> logger)
    : IPdfConverter
{
    public const string HttpClientName = "gotenberg";

    // A4 in inches, as Gotenberg expects; margins come from the page's CSS (@page)
    private static readonly IReadOnlyDictionary<string, string> Form = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["paperWidth"] = "8.27",
        ["paperHeight"] = "11.7",
        ["marginTop"] = "0",
        ["marginBottom"] = "0",
        ["marginLeft"] = "0",
        ["marginRight"] = "0",
        ["preferCssPageSize"] = "true",
        ["printBackground"] = "true",
    };

    public async Task<Result<byte[]>> ConvertHtmlAsync(string html, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        var settings = options.Value;
        using var content = new MultipartFormDataContent();
        foreach (var (name, value) in Form)
        {
            content.Add(new StringContent(value, Encoding.UTF8), name);
        }

        var page = new ByteArrayContent(Encoding.UTF8.GetBytes(html));
        page.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        content.Add(page, "files", "index.html");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var url = new Uri(new Uri(settings.GotenbergUrl.TrimEnd('/') + "/"), "forms/chromium/convert/html");
        try
        {
            using var response = await http.PostAsync(url, content, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(timeout.Token);
                LogRefused(logger, (int)response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
                return PdfErrors.Unavailable;
            }

            return await response.Content.ReadAsByteArrayAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(logger, exception, url.ToString(), settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            return PdfErrors.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Gotenberg refused the conversion with {StatusCode}: {Detail}")]
    private static partial void LogRefused(ILogger logger, int statusCode, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gotenberg at {Url} did not answer within {TimeoutSeconds} s")]
    private static partial void LogUnreachable(ILogger logger, Exception exception, string url, string timeoutSeconds);
}
