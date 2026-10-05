using Merp.ArchitectureTests.Rules;
using Merp.Infrastructure.MultiTenancy.Catalog;
using Merp.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Merp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Merp.ArchitectureTests;

/// <summary>The rules of CLAUDE.md "Mimari kurallar" plus D10, checked against the production assemblies.</summary>
public class ArchitectureTests
{
    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api", "Contracts"];

    [Fact]
    public void Discovery_FindsBuildingBlocksAndModules()
    {
        MerpAssemblies.All.Select(a => a.GetName().Name).ShouldContain("Merp.SharedKernel");
        MerpAssemblies.All.Select(a => a.GetName().Name).ShouldContain("Merp.Api");
        MerpAssemblies.All.Select(a => a.GetName().Name).ShouldContain("Merp.Migrator");
        foreach (var layer in Layers)
        {
            MerpAssemblies.All.Select(a => a.GetName().Name).ShouldContain($"Merp.Modules.Platform.{layer}");
        }
    }

    [Fact]
    public void Rule1_ModulesUseOtherModulesOnlyThroughContracts()
    {
        DependencyRules.CrossModuleReferences(DependencyRules.ReferenceGraph(MerpAssemblies.All)).ShouldBeEmpty();

        foreach (var assembly in MerpAssemblies.All.Where(a => MerpAssemblies.ModuleName(a) is not null))
        {
            var own = MerpAssemblies.ModuleName(assembly);
            var forbidden = MerpAssemblies.Modules
                .Where(m => m != own)
                .SelectMany(m => Layers.Where(l => l != "Contracts").Select(l => $"Merp.Modules.{m}.{l}"));
            DependencyRules.ForbiddenDependencies(assembly, forbidden).ShouldBeEmpty(assembly.GetName().Name);
        }
    }

    [Fact]
    public void ModuleLayers_ReferenceOnlyLowerLayers() =>
        DependencyRules.LayerReferences(DependencyRules.ReferenceGraph(MerpAssemblies.All)).ShouldBeEmpty();

    [Fact]
    public void Rule2_EachModuleOwnsOneDbContextInItsOwnSchema()
    {
        foreach (var module in MerpAssemblies.Modules)
        {
            var infrastructure = MerpAssemblies.Get($"Merp.Modules.{module}.Infrastructure");
            var contexts = infrastructure.GetTypes().Where(t => typeof(ModuleDbContext).IsAssignableFrom(t) && !t.IsAbstract).ToList();
            contexts.Count.ShouldBe(1, $"{module} must have exactly one module DbContext");

            using var context = CreateDesignTimeContext(infrastructure, contexts[0]);
            context.Schema.ShouldBe(module.ToLowerInvariant(), "the schema is the lower case module name (D2)");

            var moduleAssemblies = MerpAssemblies.All.Where(a => MerpAssemblies.ModuleName(a) == module).Append(typeof(ModuleDbContext).Assembly).ToHashSet();
            var sharedKernel = MerpAssemblies.Get("Merp.SharedKernel");
            foreach (var entity in context.Model.GetEntityTypes())
            {
                entity.GetSchema().ShouldBe(context.Schema, $"{entity.Name} must live in schema {context.Schema}");
                // Shared kernel value objects (an address) may be mapped as owned types in any module
                if (!(entity.IsOwned() && entity.ClrType.Assembly == sharedKernel))
                {
                    moduleAssemblies.ShouldContain(entity.ClrType.Assembly, $"{entity.Name} belongs to another module");
                }

                foreach (var foreignKey in entity.GetForeignKeys())
                {
                    foreignKey.PrincipalEntityType.GetSchema().ShouldBe(context.Schema, $"{entity.Name} has a foreign key into another schema");
                }
            }
        }
    }

    [Fact]
    public void ModuleSchemas_AreUnique()
    {
        var schemas = MerpAssemblies.ModuleLayer("Infrastructure")
            .SelectMany(a => a.GetTypes().Where(t => typeof(ModuleDbContext).IsAssignableFrom(t) && !t.IsAbstract))
            .Select(t => (string)t.GetProperty(nameof(IModuleSchema.SchemaName))!.GetValue(null)!)
            .ToList();

        schemas.ShouldBeUnique();
    }

    [Fact]
    public void Rule3_ContractsHoldOnlyEventsDtosAndReadOnlyQueries()
    {
        foreach (var contracts in MerpAssemblies.ModuleLayer("Contracts"))
        {
            ContractsRules.NonQueryInterfaceMethods(contracts.GetTypes()).ShouldBeEmpty(contracts.GetName().Name);
            var forbidden = DependencyRules.ForbiddenInDomain
                .Except(["Merp.Application"])
                .Concat(MerpAssemblies.Modules.SelectMany(m => Layers.Where(l => l != "Contracts").Select(l => $"Merp.Modules.{m}.{l}")));
            DependencyRules.ForbiddenDependencies(contracts, forbidden).ShouldBeEmpty(contracts.GetName().Name);
        }
    }

    [Fact]
    public void Rule4_DomainDoesNotUseInfrastructure()
    {
        foreach (var domain in MerpAssemblies.ModuleLayer("Domain").Append(MerpAssemblies.Get("Merp.SharedKernel")))
        {
            DependencyRules.ForbiddenDependencies(domain, DependencyRules.ForbiddenInDomain).ShouldBeEmpty(domain.GetName().Name);
        }
    }

    [Fact]
    public void SharedKernel_HasNoPackageDependencies() =>
        MerpAssemblies.Get("Merp.SharedKernel").GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name is not ("netstandard" or "mscorlib"))
            .ShouldBeEmpty();

    [Fact]
    public void Rule5_ApplicationDoesNotUseInfrastructure()
    {
        var moduleOuterLayers = MerpAssemblies.Modules.SelectMany(m => new[] { $"Merp.Modules.{m}.Infrastructure", $"Merp.Modules.{m}.Api" });
        foreach (var application in MerpAssemblies.ModuleLayer("Application").Append(MerpAssemblies.Get("Merp.Application.Abstractions")))
        {
            DependencyRules.ForbiddenDependencies(application, DependencyRules.ForbiddenInApplication.Concat(moduleOuterLayers))
                .ShouldBeEmpty(application.GetName().Name);
        }
    }

    [Fact]
    public void Rule7_SingleAggregatePerCommand_IsEnforcedByDefault() =>
        // Runtime check (D21); ModuleDbContextTests.CommandChangingTwoAggregates_IsRejected proves it.
        new PersistenceOptions().EnforceSingleAggregatePerCommand.ShouldBeTrue();

    [Fact]
    public void D10_NoCodeReadsTheSystemClock()
    {
        foreach (var assembly in MerpAssemblies.All)
        {
            ClockRules.SystemClockUsages(assembly).ShouldBeEmpty($"{assembly.GetName().Name} must use TimeProvider");
        }
    }

    private static ModuleDbContext CreateDesignTimeContext(System.Reflection.Assembly infrastructure, Type contextType)
    {
        var factoryInterface = typeof(IDesignTimeDbContextFactory<>).MakeGenericType(contextType);
        var factoryType = infrastructure.GetTypes().SingleOrDefault(t => factoryInterface.IsAssignableFrom(t))
            ?? throw new ShouldAssertException($"{contextType.Name} needs an IDesignTimeDbContextFactory for migrations");
        var factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
        return (ModuleDbContext)factoryInterface.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!.Invoke(factory, [Array.Empty<string>()])!;
    }
}

/// <summary>CLAUDE.md rule 6, checked against the endpoints of the real host.</summary>
public sealed class EndpointArchitectureTests(PostgresFixture postgres) : IAsyncLifetime
{
    private EndpointsHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        var catalog = await postgres.CreateDatabaseAsync();
        // The host reads the Data Protection key ring from the catalog at startup.
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, catalog);
        await using (var db = new CatalogDbContext(options.Options))
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        _host = new EndpointsHost(catalog);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public void Rule6_EveryEndpointRequiresAPermission_OrIsAllowListed()
    {
        var endpoints = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => new EndpointInfo(
                "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                e.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0,
                [.. e.Metadata.GetOrderedMetadata<RequiredPermission>().Select(p => p.Permission)],
                e.Metadata.GetMetadata<AuthenticatedUserOnly>() is not null))
            .ToList();

        endpoints.ShouldContain(e => e.Route == "/api/platform/system/info");
        endpoints.ShouldContain(e => e.Route == "/api/platform/me" && e.AuthenticatedUserOnly);
        endpoints.ShouldContain(e => e.Route == "/api/platform/localization/bundles/{culture}" && e.AllowsAnonymous);
        EndpointRules.Violations(endpoints).ShouldBeEmpty();
    }

    [Fact]
    public void Rule7_TestingEnvironmentEnforcesSingleAggregatePerCommand() =>
        _host.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value.EnforceSingleAggregatePerCommand.ShouldBeTrue();

    private sealed class EndpointsHost(string catalogConnectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Catalog", catalogConnectionString);
            builder.UseSetting("ApiDocs:Enabled", "true");
            builder.UseSetting("Jobs:EnableServer", "false");
            builder.UseSetting("Messaging:EnableBackgroundProcessing", "false");
        }
    }
}
