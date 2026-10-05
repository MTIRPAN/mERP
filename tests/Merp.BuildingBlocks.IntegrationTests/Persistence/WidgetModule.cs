using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.BuildingBlocks.IntegrationTests.Persistence;

// A miniature module used to exercise the persistence building blocks against a real PostgreSQL database.

public readonly record struct WidgetId(Guid Value) : IEntityId<WidgetId>
{
    public static WidgetId From(Guid value) => new(value);
}

public sealed record WidgetRenamed(WidgetId WidgetId, string OldName, string NewName) : IDomainEvent;

public sealed record WidgetCreated(Guid WidgetId, string Name) : IntegrationEvent;

[Audited]
public sealed class Widget : AuditableAggregateRoot<WidgetId>, ISoftDeletable
{
    private Widget()
    {
    }

    public string Name { get; private set; } = null!;

    public LocalizedString Title { get; private set; } = null!;

    [Sensitive]
    public string? Secret { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public static Widget Create(string name, string title, string? secret) => new()
    {
        Id = EntityId.New<WidgetId>(),
        Name = name,
        Title = LocalizedString.Of("tr-TR", title),
        Secret = secret,
    };

    public void Rename(string name)
    {
        Raise(new WidgetRenamed(Id, Name, name));
        Name = name;
    }

    public void ChangeSecret(string secret) => Secret = secret;
}

/// <summary>Second aggregate, updated through a domain event handler.</summary>
public sealed class WidgetLog : AggregateRoot<Guid>
{
    private WidgetLog()
    {
    }

    public WidgetId WidgetId { get; private set; }

    public string Message { get; private set; } = null!;

    public static WidgetLog Create(WidgetId widgetId, string message) => new() { Id = Guid.CreateVersion7(), WidgetId = widgetId, Message = message };
}

public sealed class WidgetDbContext(DbContextOptions<WidgetDbContext> options) : ModuleDbContext(options), IModuleSchema
{
    public static string SchemaName => "widgets";

    public override string Schema => SchemaName;

    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<WidgetLog> WidgetLogs => Set<WidgetLog>();
}

internal sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).HasMaxLength(100);
    }
}

internal sealed class WidgetLogConfiguration : IEntityTypeConfiguration<WidgetLog>
{
    public void Configure(EntityTypeBuilder<WidgetLog> builder) => builder.HasKey(l => l.Id);
}

public sealed record CreateWidget(string Name, string Title = "Başlık", string? Secret = null, bool FailAfterSave = false) : ICommand<WidgetId>;

public sealed record RenameWidget(WidgetId Id, string Name) : ICommand;

public sealed record DeleteWidget(WidgetId Id) : ICommand;

public sealed record CreateTwoWidgets : ICommand;

internal sealed class CreateWidgetHandler(WidgetDbContext db, IIntegrationEventPublisher publisher) : ICommandHandler<CreateWidget, WidgetId>
{
    public async Task<Result<WidgetId>> Handle(CreateWidget request, CancellationToken cancellationToken)
    {
        var widget = Widget.Create(request.Name, request.Title, request.Secret);
        db.Widgets.Add(widget);
        await publisher.PublishAsync(new WidgetCreated(widget.Id.Value, widget.Name), cancellationToken);

        if (request.FailAfterSave)
        {
            // Even data that already reached the database inside the transaction must disappear on rollback.
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Simulated failure");
        }

        return widget.Id;
    }
}

internal sealed class RenameWidgetHandler(WidgetDbContext db) : ICommandHandler<RenameWidget>
{
    public async Task<Result> Handle(RenameWidget request, CancellationToken cancellationToken)
    {
        var widget = await db.Widgets.FindAsync([request.Id], cancellationToken);
        if (widget is null)
        {
            return CommonErrors.NotFound;
        }

        widget.Rename(request.Name);
        return Result.Success();
    }
}

internal sealed class DeleteWidgetHandler(WidgetDbContext db) : ICommandHandler<DeleteWidget>
{
    public async Task<Result> Handle(DeleteWidget request, CancellationToken cancellationToken)
    {
        var widget = await db.Widgets.FindAsync([request.Id], cancellationToken);
        if (widget is null)
        {
            return CommonErrors.NotFound;
        }

        db.Widgets.Remove(widget);
        return Result.Success();
    }
}

internal sealed class CreateTwoWidgetsHandler(WidgetDbContext db) : ICommandHandler<CreateTwoWidgets>
{
    public Task<Result> Handle(CreateTwoWidgets request, CancellationToken cancellationToken)
    {
        db.Widgets.Add(Widget.Create("first", "Bir", null));
        db.Widgets.Add(Widget.Create("second", "İki", null));
        return Task.FromResult(Result.Success());
    }
}

internal sealed class WidgetRenamedHandler(WidgetDbContext db) : IDomainEventHandler<WidgetRenamed>
{
    public Task Handle(WidgetRenamed domainEvent, CancellationToken cancellationToken)
    {
        db.WidgetLogs.Add(WidgetLog.Create(domainEvent.WidgetId, $"{domainEvent.OldName} -> {domainEvent.NewName}"));
        return Task.CompletedTask;
    }
}
