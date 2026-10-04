using Merp.Api.Development;
using Merp.Infrastructure;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Api;
using Merp.Web.Modules;
using Merp.Web.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMerpInfrastructure();
builder.Services.AddMerpMultiTenancy(builder.Configuration);
builder.Services.AddMerpMessaging(builder.Configuration);
builder.Services.Configure<PersistenceOptions>(options =>
    options.EnforceSingleAggregatePerCommand = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

builder.Services.AddProblemDetails();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.AddModule<PlatformModule>();

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapModules();

if (app.Environment.IsDevelopment())
{
    await DevelopmentSetup.RunAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);
}

await app.RunAsync();
