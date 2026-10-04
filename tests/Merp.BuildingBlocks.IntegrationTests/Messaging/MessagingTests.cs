using Merp.BuildingBlocks.IntegrationTests.Messaging.Orders;
using Merp.BuildingBlocks.IntegrationTests.Messaging.Shipping;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.Messaging.RabbitMq;
using Merp.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.Messaging;

public sealed class MessagingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CommittedEvent_ReachesTheOtherModule_InProcess()
    {
        await using var harness = await MessagingHarness.CreateAsync(postgres);
        var orderId = (await harness.SendAsync(new PlaceOrder("ok"))).Value;

        (await harness.DispatchOutboxAsync()).ShouldBe(1);
        (await harness.ProcessShippingInboxAsync()).ShouldBe(1);

        (await harness.QueryAsync<ShippingDbContext, Guid>(db => db.Shipments.Select(s => s.OrderId).SingleAsync())).ShouldBe(orderId);
        (await harness.QueryAsync<OrdersDbContext, DateTimeOffset?>(db => db.OutboxMessages.Select(m => m.ProcessedAt).SingleAsync())).ShouldNotBeNull();
        var inbox = await harness.QueryAsync<ShippingDbContext, Infrastructure.Persistence.Inbox.InboxMessage>(db => db.InboxMessages.SingleAsync());
        inbox.Handler.ShouldBe(typeof(OrderPlacedHandler).FullName);
        inbox.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task EventOfRolledBackTransaction_IsNeverPublished()
    {
        await using var harness = await MessagingHarness.CreateAsync(postgres);

        await Should.ThrowAsync<InvalidOperationException>(() => harness.SendAsync(new PlaceOrder("rollback", FailAfterPublishing: true)));

        (await harness.DispatchOutboxAsync()).ShouldBe(0);
        (await harness.ProcessShippingInboxAsync()).ShouldBe(0);
        (await harness.QueryAsync<ShippingDbContext, int>(db => db.Shipments.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task EventDeliveredTwice_IsHandledOnce()
    {
        await using var harness = await MessagingHarness.CreateAsync(postgres);
        await harness.SendAsync(new PlaceOrder("twice"));
        var message = await harness.QueryAsync<OrdersDbContext, Infrastructure.Persistence.Outbox.OutboxMessage>(db => db.OutboxMessages.SingleAsync());
        var envelope = new MessageEnvelope(message.Id, message.Type, message.Payload, harness.Tenant.Id, message.OccurredAt, message.TraceParent);
        var transport = harness.Services.GetRequiredService<IMessageTransport>();

        await using (var scope = harness.CreateScope())
        {
            await transport.SendAsync(envelope, scope.ServiceProvider, TestContext.Current.CancellationToken);
            await transport.SendAsync(envelope, scope.ServiceProvider, TestContext.Current.CancellationToken);
        }

        await harness.DispatchOutboxAsync(); // third delivery through the dispatcher
        await harness.ProcessShippingInboxAsync();
        await harness.ProcessShippingInboxAsync();

        (await harness.QueryAsync<ShippingDbContext, int>(db => db.Shipments.CountAsync())).ShouldBe(1);
        (await harness.QueryAsync<ShippingDbContext, int>(db => db.InboxMessages.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task FailingHandler_IsRetriedWithBackoff_ThenDeadLettered()
    {
        await using var harness = await MessagingHarness.CreateAsync(postgres, maxAttempts: 3);
        await harness.SendAsync(new PlaceOrder(OrderPlacedHandler.FailingNote));
        await harness.DispatchOutboxAsync();

        async Task<Infrastructure.Persistence.Inbox.InboxMessage> Inbox() =>
            await harness.QueryAsync<ShippingDbContext, Infrastructure.Persistence.Inbox.InboxMessage>(db => db.InboxMessages.SingleAsync());

        await harness.ProcessShippingInboxAsync();
        var first = await Inbox();
        first.AttemptCount.ShouldBe(1);
        first.NextAttemptAt.ShouldBe(harness.Time.GetUtcNow().AddSeconds(2));
        first.LastError!.ShouldContain("Shipping is unavailable");

        // Not due yet: nothing happens
        await harness.ProcessShippingInboxAsync();
        (await Inbox()).AttemptCount.ShouldBe(1);

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        await harness.ProcessShippingInboxAsync();
        (await Inbox()).NextAttemptAt.ShouldBe(harness.Time.GetUtcNow().AddSeconds(4));

        harness.Time.Advance(TimeSpan.FromSeconds(4));
        await harness.ProcessShippingInboxAsync();
        var last = await Inbox();
        last.AttemptCount.ShouldBe(3);
        last.DeadLetteredAt.ShouldBe(harness.Time.GetUtcNow());
        last.ProcessedAt.ShouldBeNull();

        harness.Time.Advance(TimeSpan.FromHours(1));
        (await harness.ProcessShippingInboxAsync()).ShouldBe(0);
        (await harness.QueryAsync<ShippingDbContext, int>(db => db.Shipments.CountAsync())).ShouldBe(0);
    }
}

public sealed class RabbitMqTransportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly RabbitMqFixture _rabbitMq = new();

    public ValueTask InitializeAsync() => _rabbitMq.InitializeAsync();

    public ValueTask DisposeAsync() => _rabbitMq.DisposeAsync();

    [Fact]
    public async Task EventTravelsBetweenModules_ThroughRabbitMq()
    {
        await using var harness = await MessagingHarness.CreateAsync(postgres, _rabbitMq.Uri);
        var consumer = harness.Services.GetRequiredService<RabbitMqConsumerService>();
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await consumer.Ready.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var orderId = (await harness.SendAsync(new PlaceOrder("via-rabbit"))).Value;
        (await harness.DispatchOutboxAsync()).ShouldBe(1);

        // The consumer writes the inbox row asynchronously
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (await harness.QueryAsync<ShippingDbContext, int>(db => db.InboxMessages.CountAsync()) == 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the message did not reach the shipping inbox");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        (await harness.ProcessShippingInboxAsync()).ShouldBe(1);
        (await harness.QueryAsync<ShippingDbContext, Guid>(db => db.Shipments.Select(s => s.OrderId).SingleAsync())).ShouldBe(orderId);

        await consumer.StopAsync(TestContext.Current.CancellationToken);
    }
}
