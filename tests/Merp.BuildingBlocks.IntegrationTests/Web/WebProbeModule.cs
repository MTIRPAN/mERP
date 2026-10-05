using Merp.Application.Abstractions.Context;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Modules;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.Web;

/// <summary>Test-only endpoints that exercise the web host conventions.</summary>
public sealed class WebProbeModule : IModule
{
    public const string Permission = "probe.things.read";

    public string Name => "webprobe";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup(this);
        group.MapGet("/validation", () => Error.Validation(
            [
                new FieldError("name", CommonErrors.Required.Code),
                new FieldError("title", CommonErrors.MaxLength(10).Code, CommonErrors.MaxLength(10).Args),
            ]).ToProblem())
            .RequirePermission(Permission);
        group.MapGet("/conflict", () => Result.Failure(CommonErrors.ConcurrencyConflict).ToHttpResult()).RequirePermission(Permission);
        group.MapGet("/throw", IResult () => throw new InvalidOperationException("secret internal detail")).RequirePermission(Permission);
        group.MapGet("/company", (ICompanyContext company) => Results.Ok(company.ActiveCompanyId)).RequirePermission(Permission);

        endpoints.MapGet("/api/public/{tenantCode}/webprobe", () => Results.Ok("ok"))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.PublicPolicy);
    }
}
