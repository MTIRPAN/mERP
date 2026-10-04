using Merp.Infrastructure;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Migrates the catalog database and then every active tenant database (D3).
// Usage: Merp.Migrator [--tenant <code>]
// In Development the "demo" tenant is registered from ConnectionStrings:DemoTenant first.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddCommandLine(args, new Dictionary<string, string> { ["--tenant"] = "Migrator:Tenant" });

builder.Services.AddMerpInfrastructure();
builder.Services.AddMerpMultiTenancy(builder.Configuration);
builder.Services.AddPlatformInfrastructure();

using var host = builder.Build();
var cancellationToken = CancellationToken.None;

var catalog = host.Services.GetRequiredService<TenantCatalog>();
await catalog.MigrateAsync(cancellationToken);

var demoConnectionString = builder.Configuration.GetConnectionString("DemoTenant");
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(demoConnectionString))
{
    await catalog.EnsureTenantAsync("demo", "Demo Holding", demoConnectionString, "tr-TR", cancellationToken);
}

await host.Services.GetRequiredService<TenantDatabaseMigrator>()
    .MigrateTenantsAsync(builder.Configuration["Migrator:Tenant"], cancellationToken);
return 0;
