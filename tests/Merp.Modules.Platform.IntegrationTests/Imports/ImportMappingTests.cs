using System.Net;
using System.Net.Http.Json;
using Merp.Infrastructure.AI;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Application.Imports;
using Merp.Modules.Platform.Domain.Imports;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Web.Endpoints;

namespace Merp.Modules.Platform.IntegrationTests.Imports;

/// <summary>
/// Mapping a file's own headers to an import's columns (PLAN A9, D62): the headers are inspected with hints of their
/// values, AI suggests the columns that did not match by name from the headers alone, and the import reads the file
/// as the user mapped it.
/// </summary>
public sealed class ImportMappingTests : IClassFixture<ImportFactory>
{
    private const string Url = "/api/platform/imports/platform.branches";

    /// <summary>A customer's own headers: the first two match the template, the rest do not.</summary>
    private static readonly string[] OwnHeaders = ["Şirket kodu", "Şube kodu", "Ad", "Tip", "Şehir", "Semt", "Açık adres", "PK"];

    private readonly ImportFactory _factory;

    public ImportMappingTests(ImportFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFileWithItsOwnHeaders_IsMappedWithAiSuggestions_AndImportedAsMapped()
    {
        var root = _factory.AsRoot();
        var company = OrganizationFactory.NewCode();
        await OrganizationFactory.CreateCompanyAsync(root, OrganizationFactory.CompanyBody(company));
        var file = ImportFactory.Workbook([[company, "MAP-01", "Şube 1", "Fabrika", "Kocaeli", "Gebze", "OSB 3. Cad. No: 5", "41400"]], OwnHeaders);

        // The template's own file goes as it is; this one does not
        (await InspectAsync(root, ImportFactory.Workbook([ImportFactory.BranchRow(company, "T-1")]))).Complete.ShouldBeTrue();
        var inspection = await InspectAsync(root, file);
        inspection.ShouldSatisfyAllConditions(
            i => i.Complete.ShouldBeFalse(),
            i => i.Mapping.ShouldBe(new Dictionary<string, int> { [BranchImport.Columns.Company] = 0, [BranchImport.Columns.Code] = 1 }),
            i => i.Headers.Select(h => (h.Index, h.Text, h.Hint)).ShouldContain((7, "PK", "5-digit number")),
            i => i.Columns.ShouldContain(c => c.Key == BranchImport.Columns.Province && c.Required));

        _factory.Ai.Returns(new ImportMappingResult(
        [
            new(BranchImport.Columns.Name, 2),
            new(BranchImport.Columns.Type, 3),
            new(BranchImport.Columns.Province, 4),
            new(BranchImport.Columns.District, 5),
            new(BranchImport.Columns.Address, 6),
            new(BranchImport.Columns.PostalCode, 7),
            new(BranchImport.Columns.Code, 6),
            new("unknown", 1),
        ]));
        var response = await root.PostAsJsonAsync($"{Url}/mapping-suggestion", new { headers = inspection.Headers }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var suggestion = (await response.Content.ReadFromJsonAsync<ImportMappingSuggestion>(AiSchema.SerializerOptions, Ct))!;
        suggestion.Mapping[BranchImport.Columns.Code].ShouldBe(1);
        suggestion.Mapping[BranchImport.Columns.PostalCode].ShouldBe(7);
        suggestion.Suggested.ShouldBe([BranchImport.Columns.Name, BranchImport.Columns.Type, BranchImport.Columns.Province, BranchImport.Columns.District, BranchImport.Columns.Address, BranchImport.Columns.PostalCode]);
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(PlatformAiFeatures.ImportMapping);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<header index=\"4\" hint=\"text up to 7 characters\">Şehir</header>"),
            i => i.ShouldContain("key=\"province\""),
            i => i.ShouldNotContain("MAP-01"),
            i => i.ShouldNotContain("OSB 3. Cad."));

        // The import reads the file as mapped
        var job = await StartAsync(root, file, suggestion.Mapping);
        job.Status.ShouldBe(ImportStatus.Ready, string.Join(" | ", job.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}")));
        (await ImportFactory.CommitAsync(root, job.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ImportFactory.JobAsync(root, job.Id)).CommittedRows.ShouldBe(1);
    }

    [Fact]
    public async Task Mappings_AreChecked_AndNeedTheImportsPermission()
    {
        var root = _factory.AsRoot();
        var file = ImportFactory.Workbook([], OwnHeaders);

        var duplicate = await ImportFactory.UploadAsync(root, file, mapping: """{ "code": 1, "name": 1 }""");
        (await Problem.ReadAsync(duplicate)).Errors!.ShouldContainKey("mapping");
        var unknown = await ImportFactory.UploadAsync(root, file, mapping: """{ "nope": 1 }""");
        (await Problem.ReadAsync(unknown)).Errors!.ShouldContainKey("mapping");
        (await ImportFactory.UploadAsync(root, file, mapping: "not json")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await root.PostAsJsonAsync($"{Url}/mapping-suggestion", new { headers = Array.Empty<object>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var (reader, _) = await _factory.UserWithAsync([PlatformPermissions.Branches.Read]);
        (await ImportFactory.UploadAsync(reader, file, url: $"{Url}/inspect")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"{Url}/mapping-suggestion", new { headers = new[] { new { index = 0, text = "Ad", hint = "text" } } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ImportFactory.UploadAsync(root, file, url: "/api/platform/imports/platform.unknown/inspect")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        _factory.Ai.IsConfigured = false;
        var disabled = await root.PostAsJsonAsync($"{Url}/mapping-suggestion", new { headers = new[] { new { index = 0, text = "Ad", hint = "text" } } }, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(disabled)).Code.ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    private static async Task<ImportFileInspection> InspectAsync(HttpClient client, byte[] file)
    {
        var response = await ImportFactory.UploadAsync(client, file, url: $"{Url}/inspect");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportFileInspection>(Ct))!;
    }

    private static async Task<ImportJobDetails> StartAsync(HttpClient client, byte[] file, IReadOnlyDictionary<string, int> mapping)
    {
        var response = await ImportFactory.UploadAsync(client, file, mapping: System.Text.Json.JsonSerializer.Serialize(mapping));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var id = (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        return await ImportFactory.JobAsync(client, id);
    }
}
