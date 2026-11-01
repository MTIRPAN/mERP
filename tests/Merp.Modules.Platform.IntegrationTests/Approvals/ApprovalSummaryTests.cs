using System.Net;
using System.Net.Http.Json;
using Merp.Infrastructure.AI;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.Modules.Platform.IntegrationTests.Organization;
using static Merp.Modules.Platform.IntegrationTests.Approvals.ApprovalFactory;

namespace Merp.Modules.Platform.IntegrationTests.Approvals;

/// <summary>
/// The AI summary of an approval request (PLAN A8, D61): for the approver who decides it now or an administrator,
/// from the request's facts and history, with identifier facts and names left out.
/// </summary>
public sealed class ApprovalSummaryTests : IClassFixture<ApprovalFactory>
{
    private const string Url = "/api/platform/approvals";

    private readonly ApprovalFactory _factory;

    public ApprovalSummaryTests(ApprovalFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheApprover_GetsASummary_FromTheFactsAndTheHistory()
    {
        var company = await _factory.CompanyAsync();
        var (requesterClient, requester) = await _factory.ClientAsync();
        var first = await _factory.UserAsync();
        var (secondClient, second) = await _factory.ClientAsync();
        await _factory.DefineAsync(company, [Step(User(first)), Step(User(second))]);
        var document = await _factory.SubmitAsync(company, requester, new { amount = 12_500, category = "Seyahat", approverUserId = first, branchId = Guid.NewGuid() });
        (await _factory.DecideAsync(await _factory.TaskOfAsync(document, first), first, ApprovalDecision.Approve, "Bütçe uygun, faturalar ekte; sorular için muhasebe@acme.example")).IsSuccess.ShouldBeTrue();
        var request = (await _factory.RequestAsync(document)).Id.Value;
        _factory.Ai.Returns(new ApprovalSummaryResult(
            "12.500 TL tutarında seyahat masrafı ikinci onay adımında.",
            ["Tutar yüksek.", " "]));

        var response = await secondClient.PostAsync($"{Url}/requests/{request}/summary", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var summary = (await response.Content.ReadFromJsonAsync<ApprovalSummaryResponse>(AiSchema.SerializerOptions, Ct))!;
        summary.ShouldSatisfyAllConditions(
            s => s.Summary.ShouldBe("12.500 TL tutarında seyahat masrafı ikinci onay adımında."),
            s => s.AttentionPoints.ShouldBe(["Tutar yüksek."]),
            s => s.PromptVersion.ShouldBe(ApprovalSummaryPrompt.Version));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(PlatformAiFeatures.ApprovalSummary);
        sent.Instructions.ShouldBe(ApprovalSummaryPrompt.Instructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<summary>Masraf</summary>"),
            i => i.ShouldContain(">12500</fact>"),
            i => i.ShouldContain(">Seyahat</fact>"),
            i => i.ShouldContain("current_step=\"2\" steps=\"2\""),
            i => i.ShouldContain("kind=\"Approved\" round=\"1\" step=\"1\" by=\"approver\""),
            i => i.ShouldContain("Bütçe uygun, faturalar ekte"),
            i => i.ShouldNotContain("muhasebe@acme.example"),
            i => i.ShouldNotContain(first.ToString()),
            i => i.ShouldNotContain(second.ToString()));

        // The requester and the approver who already decided read the request, but do not decide it now
        (await requesterClient.PostAsync($"{Url}/requests/{request}/summary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var (stranger, _) = await _factory.ClientAsync();
        (await stranger.PostAsync($"{Url}/requests/{request}/summary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await secondClient.PostAsync($"{Url}/requests/{Guid.NewGuid()}/summary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // An administrator may ask too; without AI the feature is refused
        _factory.Ai.Returns(new ApprovalSummaryResult("Özet", []));
        (await _factory.AsRoot().PostAsync($"{Url}/requests/{request}/summary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Ai.IsConfigured = false;
        var disabled = await secondClient.PostAsync($"{Url}/requests/{request}/summary", null, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(disabled)).Code.ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.Count.ShouldBe(2);
    }
}
