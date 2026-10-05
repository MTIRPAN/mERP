using Merp.Application.Abstractions.Messaging;
using Merp.BuildingBlocks.IntegrationTests.Messaging.Orders;
using Merp.Infrastructure.Persistence;
using Merp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Merp.BuildingBlocks.IntegrationTests.Messaging.Shipping;

// Subscribing module: reacts to OrderPlaced from the Orders module.

public sealed class Shipment : AggregateRoot<Guid>
{
    private Shipment()
    {
    }

    public Guid OrderId { get; private set; }

    public static Shipment For(Guid orderId) => new() { Id = Guid.CreateVersion7(), OrderId = orderId };
}

public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : ModuleDbContext(options), IModuleSchema
{
    public static string SchemaName => "shipping";

    public override string Schema => SchemaName;

    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Shipment>().HasKey(s => s.Id);
    }
}

internal sealed class OrderPlacedHandler(ShippingDbContext db) : IIntegrationEventHandler<OrderPlaced>
{
    public const string FailingNote = "fail-handler";

    public Task Handle(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Note == FailingNote)
        {
            throw new InvalidOperationException("Shipping is unavailable");
        }

        db.Shipments.Add(Shipment.For(integrationEvent.OrderId));
        return Task.CompletedTask;
    }
}
