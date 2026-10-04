using Merp.Application.Abstractions.Context;
using Merp.Infrastructure;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Usage: Merp.Migrator [--connection-string <cs>] [--tenant <code>]
// Until the tenant catalog exists (PLAN 0.7) the migrator migrates one tenant database given on the command line
// or in ConnectionStrings:DemoTenant.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddCommandLine(args, new Dictionary<string, string>
{
    ["--connection-string"] = "ConnectionStrings:DemoTenant",
    ["--tenant"] = "Migrator:TenantCode",
});

builder.Services.AddMerpInfrastructure();
builder.Services.AddPlatformInfrastructure();

using var host = builder.Build();

var connectionString = builder.Configuration.GetConnectionString("DemoTenant");
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("Missing connection string: pass --connection-string or set ConnectionStrings:DemoTenant.");
    return 1;
}

var tenantCode = builder.Configuration["Migrator:TenantCode"] ?? "demo";
var tenant = new TenantInfo(Guid.Empty, tenantCode, tenantCode, "tr-TR");
await host.Services.GetRequiredService<TenantDatabaseMigrator>().MigrateAsync(tenant, connectionString, CancellationToken.None);
return 0;
