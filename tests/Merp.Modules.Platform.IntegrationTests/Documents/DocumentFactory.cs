using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Contracts.Documents;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Documents;

/// <summary>Tenant <c>acme</c> with Gotenberg for real PDFs (PLAN P12, D36).</summary>
public sealed class DocumentFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public GotenbergFixture Gotenberg { get; } = new();

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Pdf:GotenbergUrl"] = Gotenberg.Url,
        ["Pdf:TimeoutSeconds"] = "60",
    };

    public override async ValueTask InitializeAsync()
    {
        // The converter's address goes into the host's settings, so Gotenberg runs before the host starts
        await Gotenberg.InitializeAsync();
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Gotenberg.DisposeAsync();
    }

    /// <summary>An administrator of document templates and companies (Tenant scope).</summary>
    public HttpClient Administrator() =>
        CreateClientFor(
            "document-admin",
            "acme",
            PlatformPermissions.DocumentTemplates.Read,
            PlatformPermissions.DocumentTemplates.Manage,
            PlatformPermissions.Companies.Read,
            PlatformPermissions.Companies.Manage);

    public HttpClient Reader() => CreateClientFor("document-reader", "acme", PlatformPermissions.DocumentTemplates.Read);

    /// <summary>A PDF as another module asks for it, in a scope of acme.</summary>
    public async Task<GeneratedDocument> GenerateAsync(DocumentRequest request)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var result = await scope.ServiceProvider.GetRequiredService<IDocumentGenerator>().GetPdfAsync(request, TestContext.Current.CancellationToken);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.Code);
    }

    /// <summary>Audited fields of a document template in acme.</summary>
    public async Task<List<string?>> AuditedFieldsAsync(Guid templateId)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == "DocumentTemplate" && e.EntityId == templateId.ToString())
            .Select(e => e.Field)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
