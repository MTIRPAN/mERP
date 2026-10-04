using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class EntityTests
{
    [Fact]
    public void Entities_WithSameTypeAndId_AreEqual()
    {
        var id = EntityId.New<OrderId>();

        new Order(id).ShouldBe(new Order(id));
        (new Order(id) == new Order(id)).ShouldBeTrue();
        new Order(id).GetHashCode().ShouldBe(new Order(id).GetHashCode());
    }

    [Fact]
    public void Entities_WithDifferentIds_AreNotEqual() =>
        new Order(EntityId.New<OrderId>()).ShouldNotBe(new Order(EntityId.New<OrderId>()));

    [Fact]
    public void AggregateRoot_CollectsAndClearsDomainEvents()
    {
        var order = new Order(EntityId.New<OrderId>());

        order.Submit();

        order.DomainEvents.Single().ShouldBeOfType<OrderSubmitted>().OrderId.ShouldBe(order.Id);
        order.ClearDomainEvents();
        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void EntityId_New_CreatesVersion7Guids()
    {
        var first = EntityId.New<OrderId>();
        var second = EntityId.New<OrderId>();

        first.Value.Version.ShouldBe(7);
        first.ShouldNotBe(second);
    }

    [Theory]
    [InlineData("0199b0c4-7a3e-7d4f-9c2a-1b2c3d4e5f60", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("not-a-guid", false)]
    [InlineData(null, false)]
    public void EntityId_TryParse(string? value, bool expected) =>
        EntityId.TryParse<OrderId>(value, out _).ShouldBe(expected);

    private readonly record struct OrderId(Guid Value) : IEntityId<OrderId>
    {
        public static OrderId From(Guid value) => new(value);
    }

    private sealed record OrderSubmitted(OrderId OrderId) : IDomainEvent;

    private sealed class Order(OrderId id) : AggregateRoot<OrderId>(id)
    {
        public void Submit() => Raise(new OrderSubmitted(Id));
    }
}
