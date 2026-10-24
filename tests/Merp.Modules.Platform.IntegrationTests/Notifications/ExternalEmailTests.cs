using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Notifications;

/// <summary>E-mails to addresses outside the platform, e.g. to candidates (D47).</summary>
public sealed class ExternalEmailTests(NotificationFactory factory) : IClassFixture<NotificationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnExternalEmail_IsQueuedForTheAddress_WithTheTypesTextInItsLanguage()
    {
        var address = $"aday.{Guid.NewGuid().ToString("N")[..8]}@ornek.test";

        await DeliverAsync(new ExternalEmailRequested(
            "recruitment.application_received",
            address,
            "Ayşe Yılmaz",
            "tr-TR",
            new Dictionary<string, string> { ["candidate"] = "Ayşe Yılmaz", ["position"] = "Satış Uzmanı", ["company"] = "Acme" }));

        var email = await QueryAsync(db => db.EmailMessages.AsNoTracking().SingleAsync(m => m.To == address, Ct));
        email.ShouldSatisfyAllConditions(
            m => m.RecipientId.ShouldBeNull(),
            m => m.ToName.ShouldBe("Ayşe Yılmaz"),
            m => m.NotificationType.ShouldBe("recruitment.application_received"),
            m => m.Subject.ShouldBe("Satış Uzmanı başvurunuz alındı"),
            m => m.HtmlBody.ShouldContain("Sayın Ayşe Yılmaz"));
    }

    [Fact]
    public async Task AUserNotificationType_CannotGoToAnAddress()
    {
        var sending = DeliverAsync(new ExternalEmailRequested("platform.test", "biri@ornek.test", null, null, new Dictionary<string, string>()));

        await Should.ThrowAsync<InvalidOperationException>(sending);
    }

    private async Task DeliverAsync(ExternalEmailRequested message)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var handlerType = factory.Services.GetServices<IntegrationEventHandlerDescriptor>().Single(d => d.EventType == typeof(ExternalEmailRequested)).HandlerType;
        var handler = (IIntegrationEventHandler<ExternalEmailRequested>)scope.ServiceProvider.GetRequiredService(handlerType);
        await handler.Handle(message, Ct);
        await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<T> QueryAsync<T>(Func<PlatformDbContext, Task<T>> query)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<PlatformDbContext>());
    }
}
