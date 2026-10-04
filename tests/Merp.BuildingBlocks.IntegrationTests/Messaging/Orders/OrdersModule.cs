using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.BuildingBlocks.IntegrationTests.Messaging.Orders;

// Publishing module. Its contract (OrderPlaced) would live in Orders.Contracts in a real module.

public sealed record OrderPlaced(Guid OrderId, string Note) : IntegrationEvent;

public sealed class Order : AggregateRoot<Guid>
{
    private Order()
    {
    }

    public string Note { get; private set; } = null!;

    public static Order Place(string note) => new() { Id = Guid.CreateVersion7(), Note = note };
}

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : ModuleDbContext(options), IModuleSchema
{
    public static string SchemaName => "orders";

    public override string Schema => SchemaName;

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Order>().HasKey(o => o.Id);
    }
}

public sealed record PlaceOrder(string Note, bool FailAfterPublishing = false) : ICommand<Guid>;

internal sealed class PlaceOrderHandler(OrdersDbContext db, IIntegrationEventPublisher publisher) : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrder request, CancellationToken cancellationToken)
    {
        var order = Order.Place(request.Note);
        db.Orders.Add(order);
        await publisher.PublishAsync(new OrderPlaced(order.Id, order.Note), cancellationToken);
        return request.FailAfterPublishing ? throw new InvalidOperationException("Simulated failure") : order.Id;
    }
}
