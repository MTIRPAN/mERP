using System.Net;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.AI;
using Merp.Infrastructure.AI;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.AI;

/// <summary>Checks the wire format sent through the official SDK, without calling the real API.</summary>
public sealed class AnthropicModelClientTests
{
    private const string SuccessBody = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5-5",
         "content":[{"type":"text","text":"{\"summary\":\"ok\"}"}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":1200,"output_tokens":340,"cache_read_input_tokens":800,"cache_creation_input_tokens":50}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendsStructuredOutputRequest_WithEffortCachingAndFallback()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SuccessBody);
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(Request(AiEffort.High), Ct);

        handler.Uri!.AbsolutePath.ShouldBe("/v1/messages");
        handler.Headers["X-Api-Key"].ShouldBe("test-key");
        handler.Headers["anthropic-beta"].ShouldContain("server-side-fallback-2026-07-01");

        var body = handler.Body;
        body.GetProperty("model").GetString().ShouldBe("claude-sonnet-5-5");
        body.GetProperty("max_tokens").GetInt32().ShouldBe(4000);
        body.GetProperty("fallbacks").GetString().ShouldBe("default");
        body.TryGetProperty("thinking", out _).ShouldBeFalse();
        body.TryGetProperty("inference_geo", out _).ShouldBeFalse();

        var system = body.GetProperty("system")[0];
        system.GetProperty("text").GetString().ShouldBe("You extract data.");
        system.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");

        var output = body.GetProperty("output_config");
        output.GetProperty("effort").GetString().ShouldBe("high");
        output.GetProperty("format").GetProperty("type").GetString().ShouldBe("json_schema");
        output.GetProperty("format").GetProperty("schema").GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();

        var message = body.GetProperty("messages")[0];
        message.GetProperty("role").GetString().ShouldBe("user");
        message.GetProperty("content").GetString().ShouldBe("<cv>text</cv>");

        response.ShouldBe(new AiModelResponse("claude-sonnet-5-5", AiStopReason.Completed, "{\"summary\":\"ok\"}", 1200, 340, 800, 50));
    }

    [Fact]
    public async Task OmitsFallback_AndSendsInferenceGeo_WhenConfigured()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SuccessBody);
        using var client = CreateClient(handler, o =>
        {
            o.Anthropic.ServerSideFallback = false;
            o.Anthropic.InferenceGeo = "global";
        });

        await client.CompleteAsync(Request(AiEffort.Medium), Ct);

        handler.Headers.ContainsKey("anthropic-beta").ShouldBeFalse();
        handler.Body.TryGetProperty("fallbacks", out _).ShouldBeFalse();
        handler.Body.GetProperty("inference_geo").GetString().ShouldBe("global");
        handler.Body.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe("medium");
    }

    [Fact]
    public async Task MapsRefusal()
    {
        var body = SuccessBody.Replace("\"end_turn\"", "\"refusal\"", StringComparison.Ordinal);
        using var client = CreateClient(new CapturingHandler(HttpStatusCode.OK, body));

        (await client.CompleteAsync(Request(AiEffort.Low), Ct)).StopReason.ShouldBe(AiStopReason.Refusal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ProviderErrors_BecomeAiProviderException(HttpStatusCode status)
    {
        using var client = CreateClient(new CapturingHandler(status, """{"type":"error","error":{"type":"api_error","message":"boom"}}"""));

        await Should.ThrowAsync<AiProviderException>(() => client.CompleteAsync(Request(AiEffort.Medium), Ct));
    }

    [Fact]
    public void IsNotConfigured_WithoutApiKey()
    {
        using var client = CreateClient(new CapturingHandler(HttpStatusCode.OK, SuccessBody), o => o.Anthropic.ApiKey = " ");

        client.IsConfigured.ShouldBeFalse();
    }

    private static AiModelRequest Request(AiEffort effort) =>
        new("test.feature", "You extract data.", "<cv>text</cv>", AiSchema.For(typeof(Output)), effort, 4000);

    private static AnthropicModelClient CreateClient(CapturingHandler handler, Action<AiOptions>? configure = null)
    {
        var options = new AiOptions { Provider = AiProvider.Anthropic, MaxRetries = 0, Anthropic = { ApiKey = "test-key", BaseUrl = new Uri("http://anthropic.test") } };
        configure?.Invoke(options);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AnthropicModelClient.HttpClientName).Returns(_ => new HttpClient(handler));
        return new AnthropicModelClient(Options.Create(options), factory);
    }

    public sealed record Output(string Summary);

    private sealed class CapturingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public JsonElement Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = string.Join(',', header.Value);
            }

            Body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
