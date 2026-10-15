using System.Net;
using System.Text;
using Merp.Application.Abstractions.Documents;
using Merp.Infrastructure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Merp.BuildingBlocks.UnitTests.Documents;

/// <summary>The Gotenberg client (D36) against a fake server; real conversions run in the integration tests.</summary>
public sealed class GotenbergPdfConverterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendsThePageAsIndexHtml_ToTheChromiumRoute_WithA4()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()) });

        var result = await Converter(handler).ConvertHtmlAsync("<p>Çağrı Öztürk</p>", Ct);

        Encoding.ASCII.GetString(result.Value).ShouldBe("%PDF-1.7");
        handler.Uri.ShouldBe(new Uri("http://gotenberg:3000/forms/chromium/convert/html"));
        handler.Parts["paperWidth"].ShouldBe("8.27");
        handler.Parts["paperHeight"].ShouldBe("11.7");
        handler.Parts["printBackground"].ShouldBe("true");
        handler.FileName.ShouldBe("index.html");
        handler.Parts["files"].ShouldBe("<p>Çağrı Öztürk</p>");
    }

    [Fact]
    public async Task RefusedConversion_IsUnavailable()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Chromium failed") });

        var result = await Converter(handler).ConvertHtmlAsync("<p></p>", Ct);

        result.Error.ShouldBe(PdfErrors.Unavailable);
    }

    [Fact]
    public async Task UnreachableServer_IsUnavailable()
    {
        var handler = new CapturingHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await Converter(handler).ConvertHtmlAsync("<p></p>", Ct);

        result.Error.ShouldBe(PdfErrors.Unavailable);
    }

    [Fact]
    public async Task SlowServer_IsUnavailable_AfterTheTimeout()
    {
        var handler = new CapturingHandler(_ => throw new TaskCanceledException("timeout"));

        var result = await Converter(handler).ConvertHtmlAsync("<p></p>", Ct);

        result.Error.ShouldBe(PdfErrors.Unavailable);
    }

    private static GotenbergPdfConverter Converter(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new PdfOptions { GotenbergUrl = "http://gotenberg:3000/", TimeoutSeconds = 5 }),
            NullLogger<GotenbergPdfConverter>.Instance);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public Dictionary<string, string> Parts { get; } = new(StringComparer.Ordinal);

        public string? FileName { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            if (request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    Parts[name] = await part.ReadAsStringAsync(cancellationToken);
                    FileName ??= part.Headers.ContentDisposition.FileName?.Trim('"');
                }
            }

            return respond(request);
        }
    }
}
