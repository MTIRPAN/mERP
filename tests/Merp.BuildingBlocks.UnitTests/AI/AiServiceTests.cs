using System.Diagnostics.Metrics;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.AI;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Merp.BuildingBlocks.UnitTests.AI;

public sealed class AiServiceTests
{
    private const string Feature = "recruitment.cv_analysis";
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly IAiModelClient _client = Substitute.For<IAiModelClient>();
    private readonly IAiUsageLedger _ledger = Substitute.For<IAiUsageLedger>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly AiOptions _options = new()
    {
        Provider = AiProvider.Anthropic,
        Defaults = new AiTenantOptions { Enabled = true, Features = ["recruitment.*"], MonthlyBudgetUsd = 0, TransferBasis = AiTransferBasis.StandardContract },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AiServiceTests() => _client.IsConfigured.Returns(true);

    [Fact]
    public async Task Disabled_WhenNoProviderIsConfigured()
    {
        _options.Provider = AiProvider.None;

        var result = await CreateService().GenerateAsync<Output>(Request(), Ct);

        result.Error.ShouldBe(AiErrors.Disabled);
        await _client.DidNotReceive().CompleteAsync(Arg.Any<AiModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disabled_WhenProviderHasNoCredentials()
    {
        _client.IsConfigured.Returns(false);

        (await CreateService().GenerateAsync<Output>(Request(), Ct)).Error.ShouldBe(AiErrors.Disabled);
    }

    [Theory]
    [InlineData(false, "recruitment.*")]
    [InlineData(true, "hr.*")]
    public async Task Disabled_WhenTenantOrFeatureIsNotEnabled(bool enabled, string features)
    {
        _options.Tenants["acme"] = new AiTenantOptions { Enabled = enabled, Features = [features] };

        var service = CreateService();

        service.IsEnabled(Feature).ShouldBeFalse();
        (await service.GenerateAsync<Output>(Request(), Ct)).Error.ShouldBe(AiErrors.Disabled);
    }

    [Fact]
    public async Task Disabled_WhenNoKvkkTransferBasisIsRecorded()
    {
        _options.Defaults.TransferBasis = null;

        var result = await CreateService().GenerateAsync<Output>(Request(), Ct);

        result.Error.ShouldBe(AiErrors.Disabled);
        await _client.DidNotReceive().CompleteAsync(Arg.Any<AiModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BudgetExceeded_WhenMonthToDateCostReachesTheLimit()
    {
        _options.Defaults.MonthlyBudgetUsd = 5;
        _ledger.GetCostSinceAsync(AiBudgetPeriod.Start(2026, 10), Arg.Any<CancellationToken>()).Returns(5m);

        var result = await CreateService().GenerateAsync<Output>(Request(), Ct);

        result.Error.ShouldBe(AiErrors.BudgetExceeded);
        await _client.DidNotReceive().CompleteAsync(Arg.Any<AiModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Success_ReturnsTypedOutput_AndRecordsUsageWithCost()
    {
        _options.Defaults.MonthlyBudgetUsd = 5;
        _ledger.GetCostSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(4.99m);
        Respond(AiStopReason.Completed, """{"summary":"Kıdemli mühendis","score":82}""", input: 10_000, output: 2_000, cacheRead: 3_000);

        var result = await CreateService().GenerateAsync<Output>(Request(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(new Output("Kıdemli mühendis", 82));
        var expectedCost = ((10_000 * 2m) + (2_000 * 10m) + (3_000 * 0.2m)) / 1_000_000m;
        result.Value.Usage.CostUsd.ShouldBe(expectedCost);
        await _ledger.Received(1).RecordAsync(
            Arg.Is<AiUsageEntry>(e => e.Outcome == AiUsageOutcome.Succeeded
                && e.Feature == Feature
                && e.PromptVersion == "v1"
                && e.UserId == UserId
                && e.InputTokens == 10_000
                && e.CostUsd == expectedCost
                && e.EntityType == "candidate"
                && e.OccurredAt == _time.GetUtcNow()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendsSchemaOfResultType_AndConfiguredDefaults()
    {
        _options.MaxOutputTokens = 12_000;
        Respond(AiStopReason.Completed, """{"summary":"x","score":1}""");

        await CreateService().GenerateAsync<Output>(Request() with { Effort = AiEffort.High }, Ct);

        await _client.Received(1).CompleteAsync(
            Arg.Is<AiModelRequest>(r => r.OutputSchema == AiSchema.For(typeof(Output))
                && r.Effort == AiEffort.High
                && r.MaxOutputTokens == 12_000
                && r.Instructions == "instructions"
                && r.Input == "input"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AiStopReason.Refusal, AiUsageOutcome.Refused)]
    [InlineData(AiStopReason.MaxTokens, AiUsageOutcome.Truncated)]
    public async Task IncompleteResponses_FailAndAreRecorded(AiStopReason stopReason, AiUsageOutcome outcome)
    {
        Respond(stopReason, "{\"summ");

        var result = await CreateService().GenerateAsync<Output>(Request(), Ct);

        result.Error.ShouldBe(stopReason == AiStopReason.Refusal ? AiErrors.Refused : AiErrors.InvalidOutput);
        await _ledger.Received(1).RecordAsync(Arg.Is<AiUsageEntry>(e => e.Outcome == outcome && e.CostUsd > 0), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"summary":null,"score":1}""")]
    public async Task UnreadableOutput_FailsWithInvalidOutput(string text)
    {
        Respond(AiStopReason.Completed, text);

        var result = await CreateService().GenerateAsync<Output>(Request(), Ct);

        result.Error.ShouldBe(AiErrors.InvalidOutput);
        await _ledger.Received(1).RecordAsync(Arg.Is<AiUsageEntry>(e => e.Outcome == AiUsageOutcome.InvalidOutput), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProviderFailure_ReturnsUnavailable_AndRecordsFailedCall()
    {
        _client.CompleteAsync(Arg.Any<AiModelRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new AiProviderException("down"));

        var result = await CreateService().GenerateAsync<Output>(Request(), Ct);

        result.Error.ShouldBe(AiErrors.Unavailable);
        await _ledger.Received(1).RecordAsync(Arg.Is<AiUsageEntry>(e => e.Outcome == AiUsageOutcome.Failed && e.CostUsd == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABatch_IsSentWithTheSchema_WhenItsEstimateFitsTheBudget()
    {
        // Two inputs of 2 990 characters with 10 characters of instructions: 1 000 input tokens each at 2 USD per million,
        // 4 000 expected output tokens each at 10 USD per million, at half price: (2 × 0.042) / 2 = 0.042 USD
        _options.Defaults.MonthlyBudgetUsd = 5;
        _ledger.GetCostSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(4.95m);
        _client.SubmitBatchAsync(Arg.Any<IReadOnlyList<AiModelBatchItem>>(), Arg.Any<CancellationToken>()).Returns("msgbatch_1");

        var batch = await CreateService().SubmitBatchAsync<Output>(BatchRequest(), Ct);

        batch.Value.ShouldBe(new AiBatch("msgbatch_1", Feature, "v1", "application", 0.042m));
        await _client.Received(1).SubmitBatchAsync(
            Arg.Is<IReadOnlyList<AiModelBatchItem>>(items => items.Count == 2
                && items[0].CustomId == "a1"
                && items[1].Request.Input.Length == 2_990
                && items[0].Request.OutputSchema == AiSchema.For(typeof(Output))
                && items[0].Request.Instructions == "instructns"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABatch_IsHeld_WhenItsEstimateDoesNotFitTheBudget()
    {
        _options.Defaults.MonthlyBudgetUsd = 5;
        _ledger.GetCostSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(4.97m);

        (await CreateService().SubmitBatchAsync<Output>(BatchRequest(), Ct)).Error.ShouldBe(AiErrors.BudgetExceeded);
        await _client.DidNotReceive().SubmitBatchAsync(Arg.Any<IReadOnlyList<AiModelBatchItem>>(), Arg.Any<CancellationToken>());

        _options.Defaults.Features = ["hr.*"];
        (await CreateService().SubmitBatchAsync<Output>(BatchRequest(), Ct)).Error.ShouldBe(AiErrors.Disabled);
    }

    [Fact]
    public async Task BatchResults_AreReadOnceEnded_AndRecordedAtTheBatchPrice()
    {
        var batch = new AiBatch("msgbatch_1", Feature, "v1", "application", 0.042m);
        _client.GetBatchResultsAsync("msgbatch_1", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AiModelBatchResult>?)null);
        (await CreateService().GetBatchResultsAsync<Output>(batch, Ct)).Value.ShouldBeNull();

        _client.GetBatchResultsAsync("msgbatch_1", Arg.Any<CancellationToken>()).Returns(
        [
            new AiModelBatchResult("a1", new AiModelResponse("claude-sonnet-5-5", AiStopReason.Completed, """{"summary":"Uygun","score":70}""", 10_000, 2_000, 0, 0)),
            new AiModelBatchResult("a2", null),
        ]);

        var results = (await CreateService().GetBatchResultsAsync<Output>(batch, Ct)).Value!;

        results[0].ShouldSatisfyAllConditions(
            r => r.CustomId.ShouldBe("a1"),
            r => r.Response.Value.Value.ShouldBe(new Output("Uygun", 70)),
            r => r.Response.Value.Usage.CostUsd.ShouldBe(0.02m));
        results[1].Response.Error.ShouldBe(AiErrors.Unavailable);
        await _ledger.Received(1).RecordAsync(
            Arg.Is<AiUsageEntry>(e => e.Outcome == AiUsageOutcome.Succeeded && e.CostUsd == 0.02m && e.EntityType == "application" && e.EntityId == "a1"),
            Arg.Any<CancellationToken>());
        await _ledger.Received(1).RecordAsync(
            Arg.Is<AiUsageEntry>(e => e.Outcome == AiUsageOutcome.Failed && e.CostUsd == 0 && e.EntityId == "a2"),
            Arg.Any<CancellationToken>());
    }

    private static AiBatchRequest BatchRequest() => new()
    {
        Feature = Feature,
        PromptVersion = "v1",
        Instructions = "instructns",
        Items = [new AiBatchItem("a1", new string('x', 2_990)), new AiBatchItem("a2", new string('y', 2_990))],
        EntityType = "application",
    };

    private void Respond(AiStopReason stopReason, string text, long input = 1_000, long output = 100, long cacheRead = 0) =>
        _client.CompleteAsync(Arg.Any<AiModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelResponse("claude-sonnet-5-5", stopReason, text, input, output, cacheRead, 0));

    private static AiRequest Request() => new()
    {
        Feature = Feature,
        PromptVersion = "v1",
        Instructions = "instructions",
        Input = "input",
        EntityType = "candidate",
        EntityId = "42",
    };

    private AiService CreateService()
    {
        var tenant = new TenantContext();
        tenant.Set(new TenantInfo(Guid.CreateVersion7(), "acme", "Acme", "tr-TR"), "Host=unused");
        var user = new CurrentUser();
        user.SetUser(UserId, tenant.Tenant!.Id);
        var monitor = Substitute.For<IOptionsMonitor<AiOptions>>();
        monitor.CurrentValue.Returns(_options);
        var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

        return new AiService(_client, monitor, tenant, user, _ledger, new AiTelemetry(meterFactory), _time, NullLogger<AiService>.Instance);
    }

    public sealed record Output(string Summary, int Score);
}
