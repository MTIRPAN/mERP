using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Merp.Infrastructure.AI;
using Merp.Modules.HR.Application;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PageSize = UglyToad.PdfPig.Content.PageSize;

namespace Merp.Modules.Recruitment.IntegrationTests.CvAnalysis;

public sealed class RecruitmentFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
        await CreateTenantAsync("globex");
        await CreateTenantAsync("thrifty");
    }

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Ai:Tenants:globex:Enabled"] = "false",
        ["Ai:Tenants:thrifty:MonthlyBudgetUsd"] = "0.01",
    };
}

public sealed class CvAnalysisEndpointsTests : IClassFixture<RecruitmentFactory>
{
    private const string Url = "/api/recruitment/cv-analyses";

    private static readonly string[] CvLines =
    [
        "Ayse Yilmaz",
        "ayse.yilmaz@example.com - 0532 123 45 67 - linkedin.com/in/ayseyilmaz",
        "Date of birth: 01.01.1990",
        "Senior Software Engineer at Example Holding, 2018 - present",
        "Built payment services with .NET and PostgreSQL for eight years.",
    ];

    private readonly RecruitmentFactory _factory;

    public CvAnalysisEndpointsTests(RecruitmentFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnalyzesPdf_WithRedactedInput_AndReturnsAnalysis()
    {
        _factory.Ai.Returns(SampleAnalysis(withMatch: true));

        var response = await Client("acme").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf", "Backend Developer", "- 5+ years of .NET\n- PostgreSQL"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<CvAnalysisResponse>(AiSchema.SerializerOptions, Ct))!;
        body.Analysis.Summary.ShouldBe("Kıdemli backend geliştirici.");
        body.Analysis.Match!.Score.ShouldBe(100);
        body.Model.ShouldBe("claude-sonnet-5-5");
        body.PromptVersion.ShouldBe(CvAnalysisPrompt.Version);
        body.Redactions.Select(r => r.Kind).ShouldBe(["email", "phone", "url", "name", "sensitive_field"], ignoreOrder: true);

        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(RecruitmentAiFeatures.CvAnalysis);
        sent.Instructions.ShouldBe(CvAnalysisPrompt.Instructions);
        sent.Input.ShouldContain("Built payment services with .NET and PostgreSQL");
        sent.Input.ShouldContain("<requirements>\n- 5+ years of .NET\n- PostgreSQL\n</requirements>");
        sent.Input.ShouldContain("Output language: Turkish");
        foreach (var personal in new[] { "Ayse", "Yilmaz", "example.com", "0532", "linkedin", "1990" })
        {
            sent.Input.ShouldNotContain(personal);
        }
    }

    [Fact]
    public async Task AnalyzesDocx_WithoutRequirements_AndDropsAnyMatch()
    {
        _factory.Ai.Returns(SampleAnalysis(withMatch: true));

        var response = await Client("acme").PostAsync(Url, Form(Docx(CvLines), "özgeçmiş.docx"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<CvAnalysisResponse>(AiSchema.SerializerOptions, Ct))!;
        body.Analysis.Match.ShouldBeNull();
        _factory.Ai.Requests.ShouldHaveSingleItem().Input.ShouldContain("No job requirements were given");
    }

    [Fact]
    public async Task RecordsUsage_ForTheTenant()
    {
        _factory.Ai.Returns(SampleAnalysis(withMatch: false), inputTokens: 5_000, outputTokens: 1_000);
        await Client("acme").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf"), Ct);

        var summary = (await _factory.CreateClientFor("admin-1", "acme", PlatformPermissions.AiUsage.Read)
            .GetFromJsonAsync<AiUsageSummaryResponse>("/api/platform/ai-usage/summary", Ct))!;

        var usage = summary.Features.ShouldHaveSingleItem();
        usage.Feature.ShouldBe(RecruitmentAiFeatures.CvAnalysis);
        usage.Requests.ShouldBeGreaterThanOrEqualTo(1);
        usage.CostUsd.ShouldBeGreaterThanOrEqualTo(((5_000 * 2m) + (1_000 * 10m)) / 1_000_000m);
    }

    [Fact]
    public async Task MissingFile_Returns400()
    {
        var problem = await ProblemAsync(await Client("acme").PostAsync(Url, new MultipartFormDataContent { { new StringContent("x"), "jobTitle" } }, Ct), HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldContain(("file", "common.errors.required"));
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("cv.txt", "recruitment.cv.unsupported_file")]
    [InlineData("cv.pdf", "common.errors.document_unsupported_format")]
    public async Task NonCvFile_Returns400(string fileName, string code)
    {
        var problem = await ProblemAsync(await Client("acme").PostAsync(Url, Form("just text"u8.ToArray(), fileName), Ct), HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldContain(("file", code));
    }

    [Fact]
    public async Task ScannedPdfWithoutText_Returns400()
    {
        var problem = await ProblemAsync(await Client("acme").PostAsync(Url, Form(Pdf(), "scan.pdf"), Ct), HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldContain(("file", "common.errors.document_no_text"));
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TooLongRequirements_Returns400()
    {
        var form = Form(Pdf(CvLines), "cv.pdf", jobRequirements: new string('x', AnalyzeCvQuery.MaxJobRequirementsLength + 1));

        var problem = await ProblemAsync(await Client("acme").PostAsync(Url, form, Ct), HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldContain(("jobRequirements", "common.errors.max_length"));
    }

    [Fact]
    public async Task WithoutPermission_Returns403() =>
        (await _factory.CreateClientFor("user-1", "acme").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task TenantWithoutAi_Returns403_AndSendsNothing()
    {
        var problem = await ProblemAsync(await Client("globex").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf"), Ct), HttpStatusCode.Forbidden);

        problem.GetProperty("code").GetString().ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExhaustedBudget_Returns403()
    {
        _factory.Ai.Returns(SampleAnalysis(withMatch: false), inputTokens: 10_000, outputTokens: 2_000);
        (await Client("thrifty").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var problem = await ProblemAsync(await Client("thrifty").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf"), Ct), HttpStatusCode.Forbidden);

        problem.GetProperty("code").GetString().ShouldBe("common.errors.ai_budget_exceeded");
        _factory.Ai.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ProviderFailure_Returns503()
    {
        _factory.Ai.Fails();

        var problem = await ProblemAsync(await Client("acme").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf"), Ct), HttpStatusCode.ServiceUnavailable);

        problem.GetProperty("code").GetString().ShouldBe("common.errors.ai_unavailable");
    }

    [Fact]
    public async Task Refusal_Returns400()
    {
        _factory.Ai.Stops(AiStopReason.Refusal);

        var problem = await ProblemAsync(await Client("acme").PostAsync(Url, Form(Pdf(CvLines), "cv.pdf"), Ct), HttpStatusCode.BadRequest);

        problem.GetProperty("code").GetString().ShouldBe("common.errors.ai_refused");
    }

    [Fact]
    public async Task Me_ShowsCvAnalysis_OnlyWhenPermittedAndEnabled()
    {
        var enabled = (await Client("acme").GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        enabled.Ai.Features.ShouldBe(
            [
                RecruitmentAiFeatures.CvAnalysis,
                RecruitmentAiFeatures.OpeningText,
                RecruitmentAiFeatures.InterviewQuestions,
                RecruitmentAiFeatures.FeedbackSummary,
                RecruitmentAiFeatures.CandidateEmail,
                RecruitmentAiFeatures.BatchScreening,
                PlatformAiFeatures.TranslationDrafts,
                PlatformAiFeatures.ApprovalSummary,
                PlatformAiFeatures.ImportMapping,
                PlatformAiFeatures.DocumentTemplateDraft,
                PlatformAiFeatures.Assistant,
                HrAiFeatures.LeaveRequestDraft,
            ],
            ignoreOrder: true);
        var item = enabled.Menu.Single(m => m.Key == "recruitment").Children!.ShouldHaveSingleItem();
        item.Key.ShouldBe("cv-analysis");
        item.Ai.ShouldBeTrue();

        var disabled = (await Client("globex").GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        disabled.Ai.Enabled.ShouldBeFalse();
        disabled.Menu.ShouldNotContain(m => m.Key == "recruitment");

        var unpermitted = (await _factory.CreateClientFor("user-1", "acme").GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        unpermitted.Menu.ShouldNotContain(m => m.Key == "recruitment");
    }

    private HttpClient Client(string tenant) => _factory.CreateClientFor("recruiter-1", tenant, RecruitmentPermissions.Cv.Analyze);

    private static MultipartFormDataContent Form(byte[] file, string fileName, string? jobTitle = null, string? jobRequirements = null)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { content, "file", fileName } };
        if (jobTitle is not null)
        {
            form.Add(new StringContent(jobTitle), "jobTitle");
        }

        if (jobRequirements is not null)
        {
            form.Add(new StringContent(jobRequirements), "jobRequirements");
        }

        return form;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<(string Field, string Code)> FieldCodes(JsonElement problem) =>
        [.. problem.GetProperty("fieldErrors").EnumerateArray().Select(e => (e.GetProperty("field").GetString()!, e.GetProperty("code").GetString()!))];

    private static CvAnalysisResult SampleAnalysis(bool withMatch) => new(
        "Kıdemli backend geliştirici.",
        "Senior Software Engineer",
        8m,
        CvSeniority.Senior,
        [new CvExperience("Senior Software Engineer", "Example Holding", "2018", string.Empty, true, ["Ödeme servisleri"])],
        [],
        [new CvSkill(".NET", CvSkillCategory.Technical, CvSkillLevel.Expert, "8 yıl .NET")],
        [],
        [],
        withMatch ? new CvMatch(130, CvRecommendation.StrongMatch, "Uygun.", [new CvRequirementAssessment("5+ years of .NET", CvRequirementStatus.Met, "8 yıl")], ["Ödeme sistemleri"], []) : null,
        [new CvInterviewQuestion("En zor ödeme hatası?", "Derinlik")],
        []);

    private static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < lines.Length; i++)
        {
            page.AddText(lines[i], 11, new PdfPoint(40, 780 - (i * 18)), font);
        }

        return builder.Build();
    }

    private static byte[] Docx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.AddMainDocumentPart().Document = new Document(new Body(paragraphs.Select(p => new Paragraph(new Run(new Text(p))))));
        }

        return stream.ToArray();
    }
}
