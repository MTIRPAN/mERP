using Merp.Application.Abstractions.Context;
using Merp.Infrastructure;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMerpInfrastructure();
builder.Services.Configure<PersistenceOptions>(options =>
    options.EnforceSingleAggregatePerCommand = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));
builder.Services.AddPlatformInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Until the tenant catalog exists (PLAN 0.7) the development tenant database comes from configuration.
    var connectionString = app.Configuration.GetConnectionString("DemoTenant");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        await app.Services.GetRequiredService<TenantDatabaseMigrator>()
            .MigrateAsync(new TenantInfo(Guid.Empty, "demo", "Demo", "tr-TR"), connectionString, CancellationToken.None);
    }
}

await app.RunAsync();
