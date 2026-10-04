using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Messaging;
using Merp.SharedKernel.Results;

namespace Merp.BuildingBlocks.UnitTests.Messaging;

public sealed record CreateThing(string Name) : ICommand<Guid>, IRequirePermission
{
    public const string RequiredPermission = "test.things.create";

    public string Permission => RequiredPermission;
}

public sealed record GetThing(Guid Id) : IQuery<string>;

public sealed record CommandWithoutHandler : ICommand;

public sealed record ThingCreated(string Name) : IntegrationEvent;

internal sealed class CreateThingValidator : AbstractValidator<CreateThing>
{
    public CreateThingValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(10);
}

internal sealed class CreateThingHandler(Journal journal, IIntegrationEventPublisher publisher) : ICommandHandler<CreateThing, Guid>
{
    public async Task<Result<Guid>> Handle(CreateThing request, CancellationToken cancellationToken)
    {
        journal.Write("handler");
        await publisher.PublishAsync(new ThingCreated(request.Name), cancellationToken);

        return request.Name switch
        {
            "throw" => throw new InvalidOperationException("Handler failed"),
            "fail" => Error.Conflict("test.errors.duplicate"),
            _ => Guid.CreateVersion7(),
        };
    }
}

internal sealed class GetThingHandler(Journal journal) : IQueryHandler<GetThing, string>
{
    public Task<Result<string>> Handle(GetThing request, CancellationToken cancellationToken)
    {
        journal.Write("query-handler");
        return Task.FromResult(Result.Success("thing"));
    }
}

/// <summary>Records the order of pipeline events.</summary>
public sealed class Journal
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Write(string entry) => _entries.Add(entry);
}

/// <summary>Unit of work double: integration events are pending until commit and discarded on rollback.</summary>
public sealed class FakeUnitOfWork(Journal journal) : IUnitOfWork, IIntegrationEventPublisher
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<IntegrationEvent> Outbox { get; } = [];

    public Task BeginAsync(CancellationToken cancellationToken)
    {
        journal.Write("begin");
        return Task.CompletedTask;
    }

    public Task<Result> CommitAsync(CancellationToken cancellationToken)
    {
        journal.Write("commit");
        Outbox.AddRange(_pending);
        _pending.Clear();
        return Task.FromResult(Result.Success());
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        journal.Write("rollback");
        _pending.Clear();
        return Task.CompletedTask;
    }

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        _pending.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
