using Merp.Api.Development;
using Merp.Infrastructure;
using Merp.Infrastructure.AI;
using Merp.Infrastructure.Caching;
using Merp.Infrastructure.Documents;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Api;
using Merp.Modules.Platform.Application;
using Merp.Modules.Recruitment.Api;
using Merp.Web.Hosting;
using Merp.Web.Modules;
using Merp.Web.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.AddMerpObservability();
builder.Services.AddMerpInfrastructure();
builder.Services.AddMerpMultiTenancy(builder.Configuration);
builder.Services.AddMerpCaching(builder.Configuration);
builder.Services.AddMerpMessaging(builder.Configuration);
builder.Services.AddMerpAi(builder.Configuration);
builder.Services.AddMerpDocuments();
builder.AddMerpWebHost();

builder.Services.Configure<PersistenceOptions>(options =>
    options.EnforceSingleAggregatePerCommand = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

// Modules are registered explicitly (PLAN 0.9).
builder.AddModule<PlatformModule>();
builder.AddModule<RecruitmentModule>();

var app = builder.Build();

app.UseMerpWebHost();
app.MapMerpHostEndpoints(PlatformPermissions.Jobs.View);
app.MapModules();

if (app.Environment.IsDevelopment())
{
    await DevelopmentSetup.RunAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);
}

await app.RunAsync();
