using Merp.Application.Abstractions.Notifications;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Notifications;

namespace Merp.Modules.Platform.UnitTests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("/hr/leave/42", "/hr/leave/42")]
    [InlineData("  /profile ", "/profile")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Link_IsAPathInTheApplication(string? link, string? expected) =>
        Notification.SafeLink(link).ShouldBe(expected);

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public void LinksOutOfTheApplication_AreRefused(string link) =>
        Should.Throw<ArgumentException>(() => Notification.SafeLink(link));

    [Fact]
    public void MarkRead_KeepsTheFirstTime()
    {
        var notification = Notification.Create(UserId.From(Guid.CreateVersion7()), "platform.test", new Dictionary<string, string>(), null, null, Now);

        notification.MarkRead(Now.AddMinutes(1)).ShouldBeTrue();
        notification.MarkRead(Now.AddMinutes(2)).ShouldBeFalse();
        notification.ReadAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Email_IsRetriedWithGrowingDelays_ThenGivenUp()
    {
        var email = EmailMessage.Create(null, "platform.test", "ayse@acme.test", "Ayşe", "Konu", "<p>x</p>", "x", Now);
        email.NextAttemptAt.ShouldBe(Now);

        email.RecordFailure("Connection refused", Now);
        email.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
        email.RecordFailure("Connection refused", Now);
        email.NextAttemptAt.ShouldBe(Now.AddMinutes(2));

        for (var attempt = 3; attempt <= EmailMessage.MaxAttempts; attempt++)
        {
            email.RecordFailure("Connection refused", Now);
        }

        email.Status.ShouldBe(EmailStatus.Failed);
        email.AttemptCount.ShouldBe(EmailMessage.MaxAttempts);
        EmailMessage.RetryDelay(10).ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Email_Subject_IsOneLine()
    {
        var email = EmailMessage.Create(null, null, "ayse@acme.test", null, "Konu\r\nBcc: someone@evil.example", "<p>x</p>", "x", Now);

        email.Subject.ShouldBe("Konu Bcc: someone@evil.example");
    }

    [Fact]
    public void Template_NormalizesTheCulture_AndAllowsAllTypesAndCompanies()
    {
        var template = NotificationTemplate.Create(" ", null, "tr-tr", "{{ title }}", "{{ body }}");

        template.Culture.ShouldBe("tr-TR");
        template.NotificationType.ShouldBeNull();
        template.CompanyId.ShouldBeNull();
    }

    [Fact]
    public void TypeDefinition_DerivesTheTextKeysFromTheType()
    {
        var type = new NotificationTypeDefinition("hr.leave_approved", [NotificationChannel.InApp]);

        type.Module.ShouldBe("hr");
        type.TitleKey.ShouldBe("hr.notifications.leave_approved.title");
        type.BodyKey.ShouldBe("hr.notifications.leave_approved.body");
        type.LabelKey.ShouldBe("hr.notifications.leave_approved.label");
    }

    [Fact]
    public void User_GetsTheDefaults_UntilTheyChooseOtherwise()
    {
        var user = User.Create("sub-1", new UserProfile("ayse", "ayse@acme.test", "Ayşe", "Yılmaz"));
        var type = new NotificationTypeDefinition("platform.test", [NotificationChannel.InApp]);

        NotificationTypeRegistry.Receives(user, type, NotificationChannel.InApp).ShouldBeTrue();
        NotificationTypeRegistry.Receives(user, type, NotificationChannel.Email).ShouldBeFalse();

        user.SetNotificationPreference(type.Type, NotificationChannel.InApp, enabled: false, byDefault: true);
        user.SetNotificationPreference(type.Type, NotificationChannel.Email, enabled: true, byDefault: false);
        NotificationTypeRegistry.Receives(user, type, NotificationChannel.InApp).ShouldBeFalse();
        NotificationTypeRegistry.Receives(user, type, NotificationChannel.Email).ShouldBeTrue();
        user.NotificationPreferences.Count.ShouldBe(2);

        // Choosing the default again forgets the choice, so a later change of the default applies
        user.SetNotificationPreference(type.Type, NotificationChannel.InApp, enabled: true, byDefault: true);
        user.NotificationPreferences.ShouldHaveSingleItem().Channel.ShouldBe(NotificationChannel.Email);
    }

    [Fact]
    public void MandatoryTypes_IgnoreTheUsersChoice()
    {
        var user = User.Create("sub-1", new UserProfile("ayse", null, null, null));
        var type = new NotificationTypeDefinition("platform.security", [NotificationChannel.InApp, NotificationChannel.Email], Mandatory: true);
        user.SetNotificationPreference(type.Type, NotificationChannel.Email, enabled: false, byDefault: true);

        NotificationTypeRegistry.Receives(user, type, NotificationChannel.Email).ShouldBeTrue();
    }
}
