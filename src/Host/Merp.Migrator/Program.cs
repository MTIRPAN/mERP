using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure;
using Merp.Infrastructure.AI;
using Merp.Infrastructure.Documents;
using Merp.Infrastructure.Email;
using Merp.Infrastructure.Files;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application.Users;
using Merp.Modules.Platform.Infrastructure;
using Merp.Modules.Recruitment.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Migrates the catalog database and then every active tenant database (D3); afterwards the tenant data seeders
// (permission catalog, system roles) run.
// Usage:
//   Merp.Migrator [--tenant <code>]
//   Merp.Migrator create-admin --tenant <code> --username <name> --email <address> --first-name <name> --last-name <name>
//     Creates the tenant's first System Administrator in Keycloak (Keycloak:Admin settings) and e-mails the invitation.
// In Development the "demo" tenant is registered from ConnectionStrings:DemoTenant first.
var command = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !a.Contains('=', StringComparison.Ordinal));
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddCommandLine(args.Where(a => a != command).ToArray(), new Dictionary<string, string>
{
    ["--tenant"] = "Migrator:Tenant",
    ["--username"] = "Admin:UserName",
    ["--email"] = "Admin:Email",
    ["--first-name"] = "Admin:FirstName",
    ["--last-name"] = "Admin:LastName",
});

builder.Services.AddMerpInfrastructure();
builder.Services.AddMerpMultiTenancy(builder.Configuration);
// Module registrations include their request handlers, whose dependencies must resolve even though none runs here.
builder.Services.AddMerpAi(builder.Configuration);
builder.Services.AddMerpDocuments();
builder.Services.AddMerpFiles(builder.Configuration);
builder.Services.AddMerpEmail(builder.Configuration);
builder.Services.AddPlatformInfrastructure(builder.Configuration);
builder.Services.AddRecruitmentInfrastructure();

using var host = builder.Build();
var cancellationToken = CancellationToken.None;

var catalog = host.Services.GetRequiredService<TenantCatalog>();
await catalog.MigrateAsync(cancellationToken);

var demoConnectionString = builder.Configuration.GetConnectionString("DemoTenant");
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(demoConnectionString))
{
    await catalog.EnsureTenantAsync("demo", "Demo Holding", demoConnectionString, "tr-TR", cancellationToken);
}

var tenantCode = builder.Configuration["Migrator:Tenant"];
await host.Services.GetRequiredService<TenantDatabaseMigrator>().MigrateTenantsAsync(tenantCode, cancellationToken);

if (string.Equals(command, "create-admin", StringComparison.Ordinal))
{
    var admin = builder.Configuration.GetSection("Admin");
    if (tenantCode is null || admin["UserName"] is not { } userName || admin["Email"] is not { } email
        || admin["FirstName"] is not { } firstName || admin["LastName"] is not { } lastName)
    {
        await Console.Error.WriteLineAsync("create-admin needs --tenant, --username, --email, --first-name and --last-name.");
        return 2;
    }

    var tenant = await host.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(tenantCode, cancellationToken)
        ?? throw new InvalidOperationException($"Tenant '{tenantCode}' is not in the catalog.");
    await using var scope = host.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
    var result = await scope.ServiceProvider.GetRequiredService<ISender>()
        .Send(new CreateAdministratorCommand(userName, email, firstName, lastName), cancellationToken);
    if (result.IsFailure)
    {
        await Console.Error.WriteLineAsync($"create-admin failed: {result.Error.Code}");
        return 1;
    }

    Console.WriteLine($"Administrator created: {result.Value}. The invitation e-mail has been sent.");
}
else if (command is not null)
{
    await Console.Error.WriteLineAsync($"Unknown command '{command}'.");
    return 2;
}

return 0;
