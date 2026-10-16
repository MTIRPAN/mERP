using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Web;
using Merp.Web.Endpoints;

namespace Merp.Modules.Platform.IntegrationTests.Notifications;

/// <summary>E-mail templates per type, company and culture (PLAN P8, D32).</summary>
public sealed class NotificationTemplateEndpointsTests(NotificationFactory factory) : IClassFixture<NotificationFactory>
{
    private const string Url = "/api/platform/notification-templates";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompanyTemplate_IsUsed_ForNotificationsOfThatCompany()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var create = await admin.PostAsJsonAsync(Url, new
        {
            notificationType = PlatformNotificationTypes.Test,
            companyId,
            culture = "tr-TR",
            subject = "{{ company.name }}: {{ title }}",
            body = "<p>{{ recipient.name }}, {{ body }}</p><a href=\"{{ link }}\">{{ texts.open }}</a>",
        }, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));

        var subject = NotificationEndpointsTests.NewSubject();
        var email = $"{subject}@acme.test";
        var user = factory.User(subject, email);
        user.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        (await user.PostAsync("/api/platform/notifications/test", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var message = await factory.Mailpit.WaitForMessageToAsync(email, TimeSpan.FromSeconds(20), Ct);
        message.Subject.ShouldBe("Acme: Test bildirimi");
        var html = await factory.Mailpit.HtmlAsync(message.Id, Ct);
        html.ShouldContain($"<p>{subject}, Merhaba {subject}");
        html.ShouldContain("<a href=\"https://erp.acme.test/profile\">Görüntüle</a>");
    }

    [Fact]
    public async Task List_ShowsTheTemplates_AndTheBuiltInLayout()
    {
        var admin = factory.Administrator();
        var id = await CreateAsync(admin, culture: "tr-TR", subject: "Genel: {{ title }}");

        var list = (await admin.GetFromJsonAsync<NotificationTemplateList>(Url, Ct))!;

        list.Items.ShouldContain(t => t.Id == id && t.NotificationType == null && t.CompanyId == null && t.Subject == "Genel: {{ title }}");
        list.DefaultSubject.ShouldBe("{{ title }}");
        list.DefaultBody.ShouldContain("{{ texts.footer }}");
        (await admin.GetFromJsonAsync<List<NotificationTypeItem>>("/api/platform/notification-types", Ct))!
            .ShouldContain(new NotificationTypeItem(PlatformNotificationTypes.Test, "Test bildirimi"));
    }

    [Fact]
    public async Task Create_WithABrokenTemplate_Returns400WithTheLine()
    {
        var response = await factory.Administrator().PostAsJsonAsync(Url, new
        {
            culture = "tr-TR",
            subject = "{{ title }}",
            body = "<p>\n{% if body %}\n",
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["body"].ShouldHaveSingleItem().ShouldStartWith("Şablon ");
    }

    [Fact]
    public async Task Create_WithAnUnknownType_Returns400()
    {
        var response = await factory.Administrator().PostAsJsonAsync(Url, Body(type: "hr.nonexistent"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["notificationType"].ShouldBe(["Böyle bir bildirim türü yok."]);
    }

    [Fact]
    public async Task Create_ForAnInactiveCulture_Returns400()
    {
        var response = await factory.Administrator().PostAsJsonAsync(Url, Body(culture: "de-DE"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["culture"].ShouldBe(["Dil kodu geçersiz."]);
    }

    [Fact]
    public async Task Create_TheSameTypeCompanyAndCultureTwice_Returns400()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        await CreateAsync(admin, PlatformNotificationTypes.Test, companyId);

        var response = await admin.PostAsJsonAsync(Url, Body(PlatformNotificationTypes.Test, companyId), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["notificationType"].ShouldBe(["Bu değer başka bir kayıtta kullanılıyor."]);
    }

    [Fact]
    public async Task Update_AndDelete()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var id = await CreateAsync(admin, companyId: companyId);
        var version = (await admin.GetFromJsonAsync<NotificationTemplateList>(Url, Ct))!.Items.Single(t => t.Id == id).Version;

        var update = await admin.PutAsJsonAsync($"{Url}/{id}", new { version, subject = "Yeni: {{ title }}", body = "{{ body }}" }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"{Url}/{id}", new { version, subject = "Eski", body = "{{ body }}" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.AuditedFieldsAsync(id)).ShouldContain("Subject");

        (await admin.DeleteAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithoutManagePermission_Returns403()
    {
        var reader = factory.CreateClientFor("template-reader", "acme", PlatformPermissions.NotificationTemplates.Read);

        (await reader.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync(Url, Body(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.User(NotificationEndpointsTests.NewSubject()).GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithOnlyACompanyScope_Returns403()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var scoped = factory.Administrator().WithScopes(AccessScope.Company(companyId));

        var response = await scoped.PostAsJsonAsync(Url, Body(companyId: companyId), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.notifications.errors.tenant_scope_required");
    }

    private static object Body(string? type = null, Guid? companyId = null, string culture = "tr-TR", string subject = "{{ title }}") =>
        new { notificationType = type, companyId, culture, subject, body = "<p>{{ body }}</p>" };

    private static async Task<Guid> CreateAsync(HttpClient admin, string? type = null, Guid? companyId = null, string culture = "tr-TR", string subject = "{{ title }}")
    {
        var response = await admin.PostAsJsonAsync(Url, Body(type, companyId, culture, subject), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }
}
