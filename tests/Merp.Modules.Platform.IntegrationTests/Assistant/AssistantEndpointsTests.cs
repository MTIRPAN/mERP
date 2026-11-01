using System.Net;
using System.Net.Http.Json;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Assistant;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Containers;
using Merp.Testing.Web;

namespace Merp.Modules.Platform.IntegrationTests.Assistant;

public sealed class AssistantFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
    }
}

/// <summary>
/// The Ctrl+K assistant (PLAN A12, D65): the model sees the question, the actions and pages the user may use and picks
/// one; the action runs as the user and its rows never reach the model.
/// </summary>
public sealed class AssistantEndpointsTests : IClassFixture<AssistantFactory>
{
    private const string Url = "/api/platform/assistant/ask";

    private readonly AssistantFactory _factory;

    public AssistantEndpointsTests(AssistantFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient UserReader() => _factory.CreateClientFor("assistant-user", "acme", PlatformPermissions.Users.Read);

    private static AssistantPlan Plan(
        AssistantPlanKind kind,
        string actionKey = "",
        string pageKey = "",
        string title = "Başlık",
        string message = "",
        params AssistantPlanArgument[] arguments) =>
        new(kind, actionKey, arguments, pageKey, title, message);

    [Fact]
    public async Task APage_TheUserMayOpen_IsOpened_AndOnlyTheirActionsAndPagesAreOffered()
    {
        _factory.Ai.Returns(Plan(AssistantPlanKind.Page, pageKey: "users", title: "Kullanıcılar"));

        var response = await AskAsync(UserReader(), "Kullanıcı listesini aç, sorun olursa ali@acme.example");

        response.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(AssistantResponseKind.Page),
            r => r.Path.ShouldBe("/access/users"),
            r => r.Title.ShouldBe("Kullanıcılar"),
            r => r.Answer.ShouldBeNull(),
            r => r.PromptVersion.ShouldBe(AssistantPrompt.Version));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(PlatformAiFeatures.Assistant);
        sent.Instructions.ShouldBe(AssistantPrompt.Instructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldStartWith("Output language: Turkish"),
            i => i.ShouldContain("<action key=\"platform.my_approval_tasks\">"),
            i => i.ShouldNotContain("<action key=\"hr.employees\">"),
            i => i.ShouldContain("<page key=\"users\" section=\""),
            i => i.ShouldContain(">Kullanıcılar</page>"),
            i => i.ShouldNotContain("<page key=\"companies\""),
            i => i.ShouldContain("Kullanıcı listesini aç"),
            i => i.ShouldNotContain("ali@acme.example"));
    }

    [Fact]
    public async Task AnAction_RunsAsTheUser_AndItsRowsComeBack()
    {
        _factory.Ai.Returns(Plan(AssistantPlanKind.Action, actionKey: "platform.my_approval_tasks", title: "Onayımı bekleyenler", arguments: new AssistantPlanArgument("unknown", "x")));

        var response = await AskAsync(UserReader(), "Onayımı bekleyen ne var?");

        response.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(AssistantResponseKind.Answer),
            r => r.Title.ShouldBe("Onayımı bekleyenler"),
            r => r.Answer!.Total.ShouldBe(0),
            r => r.Answer!.Items.ShouldBeEmpty(),
            r => r.Answer!.Link.ShouldBe("/approvals"));
    }

    [Fact]
    public async Task WhatTheUserMayNotUse_OrNothingFitting_AnswersNone()
    {
        _factory.Ai.Returns(Plan(AssistantPlanKind.Page, pageKey: "companies"));
        _factory.Ai.Returns(Plan(AssistantPlanKind.Action, actionKey: "hr.employees"));
        _factory.Ai.Returns(Plan(AssistantPlanKind.None, message: "Kayıt değiştiremem; onay görevlerinizi listeleyebilirim."));

        var page = await AskAsync(UserReader(), "Şirketleri aç");
        var action = await AskAsync(UserReader(), "Ayşe kimdir?");
        var none = await AskAsync(UserReader(), "Ayşe'nin maaşını artır");

        page.ShouldSatisfyAllConditions(p => p.Kind.ShouldBe(AssistantResponseKind.None), p => p.Path.ShouldBeNull(), p => p.Message.ShouldBeNull());
        action.ShouldSatisfyAllConditions(a => a.Kind.ShouldBe(AssistantResponseKind.None), a => a.Answer.ShouldBeNull());
        none.ShouldSatisfyAllConditions(n => n.Kind.ShouldBe(AssistantResponseKind.None), n => n.Message.ShouldBe("Kayıt değiştiremem; onay görevlerinizi listeleyebilirim."));
    }

    [Fact]
    public async Task Questions_AreChecked_AndNeedTheFeature()
    {
        var client = UserReader();
        (await Problem.ReadAsync(await client.PostAsJsonAsync(Url, new { text = "" }, Ct))).Errors!.ShouldContainKey("text");
        (await client.PostAsJsonAsync(Url, new { text = new string('a', AskAssistantQuery.TextMaxLength + 1) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        _factory.Ai.IsConfigured = false;
        var disabled = await client.PostAsJsonAsync(Url, new { text = "Onay görevlerim" }, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(disabled)).Code.ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    private static async Task<AssistantResponse> AskAsync(HttpClient client, string text)
    {
        var response = await client.PostAsJsonAsync(Url, new { text }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<AssistantResponse>(Ct))!;
    }
}
