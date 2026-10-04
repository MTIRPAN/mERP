using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Web.Modules;

/// <summary>
/// Entry point of a module in the web host. Modules are registered explicitly in <c>Program.cs</c> with
/// <see cref="ModuleExtensions.AddModule{TModule}"/>.
/// </summary>
public interface IModule
{
    /// <summary>Lower case module name; also the endpoint prefix <c>/api/{name}</c> (D2).</summary>
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
