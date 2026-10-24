using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Application.Imports;
using Merp.Testing.Containers;
using Merp.Web.Endpoints;

namespace Merp.Modules.HR.IntegrationTests.Imports;

/// <summary>
/// HR's imports through Platform's import endpoints (PLAN H4, D40). Files up to 10,000 rows are handled within the
/// request: the test scopes are the caller's, and the background path is covered by Platform's import tests.
/// </summary>
public sealed class HrImportFactory(PostgresFixture postgres) : HrFactory(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Platform:Imports:BackgroundThreshold"] = "10000",
    };

    /// <summary>A workbook with the column keys as headers, which the import reads as well as the template's headers.</summary>
    public static byte[] Workbook(IReadOnlyList<Dictionary<string, string?>> rows)
    {
        var keys = rows.SelectMany(r => r.Keys).Distinct(StringComparer.Ordinal).ToList();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Veri");
        for (var c = 0; c < keys.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = keys[c];
            for (var r = 0; r < rows.Count; r++)
            {
                if (rows[r].GetValueOrDefault(keys[c]) is { } value)
                {
                    sheet.Cell(r + 2, c + 1).SetValue(value);
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string key, byte[] content, string? mode = null)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var form = new MultipartFormDataContent { { file, "file", "dosya.xlsx" } };
        if (mode is not null)
        {
            form.Add(new StringContent(mode), "mode");
        }

        return await client.PostAsync($"/api/platform/imports/{key}", form, Ct);
    }

    /// <summary>Uploads a file and returns its validated job.</summary>
    public static async Task<ImportJobDetails> StartAsync(HttpClient client, string key, byte[] content, string? mode = null)
    {
        var response = await UploadAsync(client, key, content, mode);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var id = (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        return await JobAsync(client, id);
    }

    public static async Task<ImportJobDetails> JobAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ImportJobDetails>($"/api/platform/imports/jobs/{id}", Ct))!;

    /// <summary>Commits a job and returns it afterwards.</summary>
    public static async Task<ImportJobDetails> CommitAsync(HttpClient client, ImportJobDetails job, bool skipInvalidRows = false)
    {
        var url = $"/api/platform/imports/jobs/{job.Id}/commit" + (skipInvalidRows ? "?skipInvalidRows=true" : string.Empty);
        (await client.PostAsync(url, null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return await JobAsync(client, job.Id);
    }

    /// <summary>The errors of a job, readable in a failed assertion.</summary>
    public static string Describe(ImportJobDetails job) => string.Join(" | ", job.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}"));

    /// <summary>The folder holding <c>Merp.slnx</c>, where <c>samples/</c> is.</summary>
    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Merp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Merp.slnx was not found above the test output.");
    }
}
