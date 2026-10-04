using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Messaging;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.Messaging;

public class UnitOfWorkRegistryTests
{
    [Fact]
    public async Task Command_FromModuleWithoutUnitOfWork_FailsFast()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMerpMediator();
        services.AddRequestHandlersFromAssembly(typeof(UnitOfWorkRegistryTests).Assembly);
        services.AddScoped<Journal>();
        services.AddScoped<IIntegrationEventPublisher, FakeUnitOfWork>();
        services.AddSingleton(Substitute.For<ICurrentUser>());
        var permissions = Substitute.For<IPermissionChecker>();
        permissions.HasPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(permissions);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateThing("desk"), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("No unit of work is registered");
    }

    [Fact]
    public void AddModuleUnitOfWork_BeforeMediator_Throws() =>
        Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddModuleUnitOfWork<FakeUnitOfWork>(typeof(UnitOfWorkRegistryTests).Assembly));

    [Fact]
    public void Register_SameAssemblyTwice_Throws()
    {
        var registry = new UnitOfWorkRegistry();
        registry.Register(typeof(UnitOfWorkRegistryTests).Assembly, typeof(FakeUnitOfWork));

        Should.Throw<InvalidOperationException>(() => registry.Register(typeof(UnitOfWorkRegistryTests).Assembly, typeof(FakeUnitOfWork)));
    }

    [Fact]
    public void ResultFactory_CreatesTypedFailures()
    {
        var error = Error.Conflict("test.errors.any");

        ResultFactory.Failure<Result>(error).Error.ShouldBe(error);
        ResultFactory.Failure<Result<int>>(error).Error.ShouldBe(error);
    }
}
