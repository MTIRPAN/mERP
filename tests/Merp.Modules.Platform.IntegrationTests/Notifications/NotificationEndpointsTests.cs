using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Application.Settings;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Microsoft.AspNetCore.SignalR.Client;

namespace Merp.Modules.Platform.IntegrationTests.Notifications;

/// <summary>PLAN P8 acceptance: a notification reaches the bell at once and Mailpit by e-mail, as the user's preferences allow.</summary>
public sealed class NotificationEndpointsTests(NotificationFactory factory) : IClassFixture<NotificationFactory>
{
    private const string Url = "/api/platform/notifications";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Notification_ArrivesAtOnce_InTheBell_AndByEmail()
    {
        var subject = NewSubject();
        var email = $"{subject}@acme.test";
        var client = factory.User(subject, email);
        await using var hub = await factory.ConnectAsync(subject, email);
        var pushed = new TaskCompletionSource<NotificationPushed>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<NotificationPushed>("notificationReceived", pushed.SetResult);

        (await client.PostAsync($"{Url}/test", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var push = await pushed.Task.WaitAsync(Wait, Ct);
        push.Type.ShouldBe(PlatformNotificationTypes.Test);

        var list = (await client.GetFromJsonAsync<PagedResult<NotificationItem>>(Url, Ct))!;
        var item = list.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(push.Id);
        item.Title.ShouldBe("Test bildirimi");
        item.Body.ShouldBe($"Merhaba {subject}, bildirimleriniz bu kanaldan size ulaşıyor.");
        item.Link.ShouldBe("/profile");
        item.ReadAt.ShouldBeNull();
        (await client.GetFromJsonAsync<UnreadNotificationCount>($"{Url}/unread-count", Ct))!.Count.ShouldBe(1);

        var message = await factory.Mailpit.WaitForMessageToAsync(email, Wait, Ct);
        message.Subject.ShouldBe("Test bildirimi");
        var html = await factory.Mailpit.HtmlAsync(message.Id, Ct);
        html.ShouldContain("https://erp.acme.test/profile");
        html.ShouldContain($"Merhaba {subject}");
    }

    [Fact]
    public async Task UserWhoSwitchedEmailOff_GetsOnlyTheInAppNotification()
    {
        var subject = NewSubject();
        var email = $"{subject}@acme.test";
        var client = factory.User(subject, email);
        var preferences = new { preferences = new[] { new { type = PlatformNotificationTypes.Test, inApp = true, email = false } } };
        (await client.PutAsJsonAsync($"{Url}/preferences", preferences, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var saved = (await client.GetFromJsonAsync<List<NotificationPreferenceItem>>($"{Url}/preferences", Ct))!;
        saved.ShouldContain(new NotificationPreferenceItem(PlatformNotificationTypes.Test, "Test bildirimi", false, true, false));

        await using var hub = await factory.ConnectAsync(subject);
        var pushed = new TaskCompletionSource<NotificationPushed>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<NotificationPushed>("notificationReceived", pushed.SetResult);
        (await client.PostAsync($"{Url}/test", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await pushed.Task.WaitAsync(Wait, Ct);
        // E-mails are sent right after the commit that also pushes; give a stray one time to arrive
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await factory.Mailpit.MessagesToAsync(email, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TenantWithEmailOff_GetsNotificationsOnlyInTheBell()
    {
        var subject = NewSubject();
        var email = $"{subject}@acme.test";
        var client = factory.User(subject, email);
        var settings = factory.CreateClientFor("settings-admin", "acme", PlatformPermissions.Settings.Read, PlatformPermissions.Settings.Manage);
        var off = new { values = new Dictionary<string, object?> { [PlatformSettingDefinitions.EmailNotificationsEnabled] = false } };
        (await settings.PutAsJsonAsync("/api/platform/settings", off, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        try
        {
            await using var hub = await factory.ConnectAsync(subject, email);
            var pushed = new TaskCompletionSource<NotificationPushed>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.On<NotificationPushed>("notificationReceived", pushed.SetResult);
            (await client.PostAsync($"{Url}/test", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            await pushed.Task.WaitAsync(Wait, Ct);
            await Task.Delay(TimeSpan.FromSeconds(2), Ct);
            (await factory.Mailpit.MessagesToAsync(email, Ct)).ShouldBeEmpty();
        }
        finally
        {
            var back = new { values = new Dictionary<string, object?> { [PlatformSettingDefinitions.EmailNotificationsEnabled] = null } };
            (await settings.PutAsJsonAsync("/api/platform/settings", back, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task MarkRead_AndMarkAllRead_EmptyTheBell()
    {
        var subject = NewSubject();
        var client = factory.User(subject);
        await SendTestAndWaitAsync(client, subject);
        await SendTestAndWaitAsync(client, subject);
        var items = (await client.GetFromJsonAsync<PagedResult<NotificationItem>>(Url, Ct))!.Items;
        items.Count.ShouldBe(2);

        (await client.PostAsync($"{Url}/{items[0].Id}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<UnreadNotificationCount>($"{Url}/unread-count", Ct))!.Count.ShouldBe(1);
        (await client.GetFromJsonAsync<PagedResult<NotificationItem>>($"{Url}?unreadOnly=true", Ct))!.Items.ShouldHaveSingleItem().Id.ShouldBe(items[1].Id);

        (await client.PostAsync($"{Url}/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<UnreadNotificationCount>($"{Url}/unread-count", Ct))!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task AnotherUsersNotification_IsNotFound()
    {
        var owner = NewSubject();
        var ownerClient = factory.User(owner);
        await SendTestAndWaitAsync(ownerClient, owner);
        var id = (await ownerClient.GetFromJsonAsync<PagedResult<NotificationItem>>(Url, Ct))!.Items[0].Id;
        var stranger = factory.User(NewSubject());

        (await stranger.PostAsync($"{Url}/{id}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetFromJsonAsync<PagedResult<NotificationItem>>(Url, Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Preferences_ForAnUnknownType_Return400()
    {
        var client = factory.User(NewSubject());
        var preferences = new { preferences = new[] { new { type = "hr.nonexistent", inApp = false, email = false } } };

        var response = await client.PutAsJsonAsync($"{Url}/preferences", preferences, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["preferences[0].type"].ShouldBe(["Böyle bir bildirim türü yok."]);
    }

    [Fact]
    public async Task AnonymousCaller_Gets401()
    {
        var response = await factory.CreateClient().GetAsync(Url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    internal static string NewSubject() => "u" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Sends the caller a test notification and waits until it is in their bell.</summary>
    private async Task SendTestAndWaitAsync(HttpClient client, string subject)
    {
        await using var hub = await factory.ConnectAsync(subject);
        var pushed = new TaskCompletionSource<NotificationPushed>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<NotificationPushed>("notificationReceived", pushed.SetResult);
        (await client.PostAsync($"{Url}/test", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await pushed.Task.WaitAsync(Wait, Ct);
    }
}
