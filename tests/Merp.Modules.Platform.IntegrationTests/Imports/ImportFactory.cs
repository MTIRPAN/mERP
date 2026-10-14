using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Imports;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Modules.Platform.IntegrationTests.Access;
using Merp.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Imports;

/// <summary>
/// Real permission checks and data scopes (a large import is validated in the background, where the user's scope is
/// read by their id), and a low background threshold so the background path runs with few rows.
/// </summary>
public sealed class ImportFactory(PostgresFixture postgres) : AccessFactory(postgres)
{
    public const string BranchesUrl = "/api/platform/imports/platform.branches";

    public const int BackgroundThreshold = 20;

    public static readonly string[] BranchHeaders =
        ["Şirket kodu *", "Şube kodu *", "Şube adı *", "Tür *", "İl *", "İlçe *", "Adres *", "Posta kodu", "Telefon", "SGK işyeri sicil no"];

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Platform:Imports:BackgroundThreshold"] = BackgroundThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>A workbook with <paramref name="headers"/> and <paramref name="rows"/>, as a user fills the template.</summary>
    public static byte[] Workbook(IEnumerable<string?[]> rows, string[]? headers = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Şubeler");
        var all = new[] { headers ?? BranchHeaders }.Concat(rows).ToList();
        for (var r = 0; r < all.Count; r++)
        {
            for (var c = 0; c < all[r].Length; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = all[r][c];
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static string?[] BranchRow(string company, string code, string type = "Fabrika", string province = "Kocaeli", string district = "Gebze") =>
        [company, code, "Şube " + code, type, province, district, "OSB 3. Cad. No: 5", "41400", "0262 123 45 67", null];

    public static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string url = BranchesUrl, string fileName = "subeler.xlsx")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return client.PostAsync(url, new MultipartFormDataContent { { file, "file", fileName } }, TestContext.Current.CancellationToken);
    }

    /// <summary>Uploads and returns the job as it stands after the request (background work runs inline in tests).</summary>
    public static async Task<ImportJobDetails> StartAsync(HttpClient client, byte[] content)
    {
        var response = await UploadAsync(client, content);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var id = (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
        return await JobAsync(client, id);
    }

    public static async Task<ImportJobDetails> JobAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ImportJobDetails>($"/api/platform/imports/jobs/{id}", TestContext.Current.CancellationToken))!;

    public static Task<HttpResponseMessage> CommitAsync(HttpClient client, Guid id) =>
        client.PostAsync($"/api/platform/imports/jobs/{id}/commit", null, TestContext.Current.CancellationToken);

    /// <summary>Runs <paramref name="query"/> against the Platform database of <paramref name="tenantCode"/>.</summary>
    public async Task<T> QueryAsync<T>(Func<PlatformDbContext, Task<T>> query, string tenantCode = "acme")
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(tenantCode, TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<PlatformDbContext>());
    }
}
