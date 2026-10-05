using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Messaging.Behaviors;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.Messaging;

public sealed class MediatorPipelineTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPermissionChecker _permissionChecker = Substitute.For<IPermissionChecker>();

    public MediatorPipelineTests()
    {
        _permissionChecker.HasPermissionAsync(CreateThing.RequiredPermission, Arg.Any<CancellationToken>()).Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMerpMediator();
        services.AddRequestHandlersFromAssembly(typeof(MediatorPipelineTests).Assembly);
        services.AddScoped<Journal>();
        services.AddScoped<FakeUnitOfWork>();
        services.AddScoped<IIntegrationEventPublisher>(sp => sp.GetRequiredService<FakeUnitOfWork>());
        services.AddModuleUnitOfWork<FakeUnitOfWork>(typeof(MediatorPipelineTests).Assembly);
        services.AddSingleton(_currentUser);
        services.AddSingleton(_permissionChecker);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ISender Sender => _scope.ServiceProvider.GetRequiredService<ISender>();

    private Journal Journal => _scope.ServiceProvider.GetRequiredService<Journal>();

    private FakeUnitOfWork UnitOfWork => _scope.ServiceProvider.GetRequiredService<FakeUnitOfWork>();

    [Fact]
    public void Behaviors_AreRegisteredInPipelineOrder()
    {
        var behaviors = _scope.ServiceProvider.GetServices<IPipelineBehavior<CreateThing, Result<Guid>>>()
            .Select(b => b.GetType().GetGenericTypeDefinition());

        behaviors.ShouldBe(
        [
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(UnitOfWorkBehavior<,>),
        ]);
    }

    [Fact]
    public async Task ValidCommand_RunsInsideUnitOfWork_AndCommitsOutbox()
    {
        var result = await Sender.Send(new CreateThing("desk"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(7);
        Journal.Entries.ShouldBe(["begin", "handler", "commit", "after-commit"]);
        UnitOfWork.Outbox.ShouldHaveSingleItem().ShouldBeOfType<ThingCreated>().Name.ShouldBe("desk");
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("throw")]
    public async Task FailedCommand_DropsItsAfterCommitActions(string name)
    {
        try
        {
            (await Sender.Send(new CreateThing(name), Ct)).IsFailure.ShouldBeTrue();
        }
        catch (InvalidOperationException)
        {
            // the handler threw on purpose
        }

        Journal.Entries.ShouldNotContain("after-commit");
        (await Sender.Send(new CreateThing("desk"), Ct)).IsSuccess.ShouldBeTrue();
        Journal.Entries.Count(e => e == "after-commit").ShouldBe(1);
    }

    [Fact]
    public async Task InvalidCommand_ReturnsValidationResult_WithoutReachingLaterBehaviors()
    {
        var result = await Sender.Send(new CreateThing("a name that is too long"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        var field = result.Error.Fields.ShouldHaveSingleItem();
        field.Field.ShouldBe("name");
        field.Code.ShouldBe("common.errors.max_length");
        field.Args["maxLength"].ShouldBe(10);
        field.Args.ShouldNotContainKey("propertyValue");

        await _permissionChecker.DidNotReceive().HasPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Journal.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task EmptyName_MapsToRequired()
    {
        var result = await Sender.Send(new CreateThing(""), Ct);

        result.Error.Fields.ShouldHaveSingleItem().Code.ShouldBe("common.errors.required");
    }

    [Fact]
    public async Task UserWithoutPermission_GetsForbidden_BeforeUnitOfWorkStarts()
    {
        _permissionChecker.HasPermissionAsync(CreateThing.RequiredPermission, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Sender.Send(new CreateThing("desk"), Ct);

        result.Error.ShouldBe(CommonErrors.Forbidden);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        Journal.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task SystemCaller_SkipsPermissionCheck()
    {
        _currentUser.IsSystem.Returns(true);
        _permissionChecker.HasPermissionAsync(CreateThing.RequiredPermission, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Sender.Send(new CreateThing("desk"), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task HandlerException_RollsBack_AndOutboxStaysEmpty()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => Sender.Send(new CreateThing("throw"), Ct));

        Journal.Entries.ShouldBe(["begin", "handler", "rollback"]);
        UnitOfWork.Outbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task FailedResult_RollsBack_AndOutboxStaysEmpty()
    {
        var result = await Sender.Send(new CreateThing("fail"), Ct);

        result.Error.Code.ShouldBe("test.errors.duplicate");
        Journal.Entries.ShouldBe(["begin", "handler", "rollback"]);
        UnitOfWork.Outbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_DoesNotOpenUnitOfWork()
    {
        var result = await Sender.Send(new GetThing(Guid.CreateVersion7()), Ct);

        result.Value.ShouldBe("thing");
        Journal.Entries.ShouldBe(["query-handler"]);
    }

    [Fact]
    public async Task RequestWithoutHandler_Throws()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => Sender.Send(new CommandWithoutHandler(), Ct));

        exception.Message.ShouldContain(nameof(CommandWithoutHandler));
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _provider.DisposeAsync();
    }
}
