using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Users;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Access;

public sealed class UserEndpointsTests(AccessFactory factory) : IClassFixture<AccessFactory>
{
    private const string Url = "/api/platform/users";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object NewUserBody(string? userName = null, string? email = null) => new
    {
        userName = userName ?? AccessFactory.NewSubject("kullanici."),
        email = email ?? $"{Guid.NewGuid():N}@example.com",
        firstName = "Ayşe",
        lastName = "Yılmaz",
        culture = "tr-TR",
    };

    [Fact]
    public async Task Create_MakesTheIdentityAndTheUser_AndSendsTheInvitationAfterCommit()
    {
        var response = await factory.AsRoot().PostAsJsonAsync(Url, NewUserBody("Ayse.Yilmaz", "AYSE@example.com"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var id = (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        var user = await factory.UserAsync(id);
        user.UserName.ShouldBe("ayse.yilmaz");
        user.Email.ShouldBe("ayse@example.com");
        user.DisplayName.ShouldBe("Ayşe Yılmaz");
        user.Culture.ShouldBe("tr-TR");

        var identity = factory.Identity.Identities.Values.Single(i => i.UserName == "ayse.yilmaz");
        identity.TenantCode.ShouldBe("acme");
        factory.Identity.Emails.ShouldContain(e => e.Email == IdentityEmail.Invitation && factory.Identity.Identities[e.Subject] == identity);
    }

    [Fact]
    public async Task Create_WithInvalidOrTakenData_Returns400()
    {
        var invalid = await factory.AsRoot().PostAsJsonAsync(Url, new { userName = "ayşe yılmaz", email = "x@", firstName = "", lastName = "Y" }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["userName", "email", "firstName"], ignoreOrder: true);

        // What Keycloak would refuse is caught first: too short a user name, characters it does not allow in names
        var keycloakRules = await factory.AsRoot().PostAsJsonAsync(Url, new { userName = "mt", email = "mt@example.com", firstName = "Ali (IT)", lastName = "Y" }, Ct);
        var errors = (await Problem.ReadAsync(keycloakRules)).Errors!;
        errors["userName"].ShouldBe(["3 ile 255 karakter arasında olmalıdır."]);
        errors["firstName"].ShouldBe(["Ad ve soyad özel karakter içeremez; örneğin parantez, köşeli parantez, < > & $ % ! # ; * / | = kullanılamaz."]);
        errors.ContainsKey("lastName").ShouldBeFalse();

        var userName = AccessFactory.NewSubject("dup.");
        (await factory.AsRoot().PostAsJsonAsync(Url, NewUserBody(userName), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var taken = await factory.AsRoot().PostAsJsonAsync(Url, NewUserBody(userName), Ct);
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(taken)).Errors!["userName"].ShouldBe(["Bu değer başka bir kayıtta kullanılıyor."]);
    }

    [Fact]
    public async Task Create_WhenKeycloakRefusesOrIsDown_ReturnsItsError_AndStoresNothing()
    {
        var userName = AccessFactory.NewSubject("kc.");
        factory.Identity.NextError = IdentityErrors.AlreadyExists;
        (await factory.AsRoot().PostAsJsonAsync(Url, NewUserBody(userName), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        factory.Identity.NextError = IdentityErrors.Unavailable;
        (await factory.AsRoot().PostAsJsonAsync(Url, NewUserBody(userName), Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var list = (await factory.AsRoot().GetFromJsonAsync<PagedResult<UserListItem>>($"{Url}?search={userName}", Ct))!;
        list.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task UserAdministration_NeedsThePermission_WithTheTenantScope()
    {
        var company = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var (companyAdmin, _) = await factory.UserWithAsync([PlatformPermissions.Users.Read, PlatformPermissions.Users.Manage], "Company", company);
        var (reader, _) = await factory.UserWithAsync([PlatformPermissions.Users.Read]);
        var nobody = factory.As(AccessFactory.NewSubject());

        var scoped = await companyAdmin.GetAsync(Url, Ct);
        scoped.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(scoped)).Code.ShouldBe("platform.users.errors.tenant_scope_required");
        (await companyAdmin.PostAsJsonAsync(Url, NewUserBody(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await reader.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync(Url, NewUserBody(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/platform/scope-types", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_FiltersByTextStateAndRole_AndShowsCurrentRoles()
    {
        var marker = AccessFactory.NewSubject("liste");
        var holder = await factory.UserIdAsync(marker + "a");
        var inactive = await factory.UserIdAsync(marker + "b");
        var roleId = await factory.CreateRoleAsync([PlatformPermissions.Branches.Read]);
        await factory.AssignAsync(holder, roleId);
        await factory.AsRoot().PutAsJsonAsync($"{Url}/{inactive}/active", new { isActive = false }, Ct);

        var all = (await factory.AsRoot().GetFromJsonAsync<PagedResult<UserListItem>>($"{Url}?search={marker}&sort=-userName", Ct))!;
        all.Items.Select(u => u.Id).ShouldBe([inactive, holder]);
        all.Items[1].Roles.ShouldBe(["Test rolü"]);

        (await factory.AsRoot().GetFromJsonAsync<PagedResult<UserListItem>>($"{Url}?search={marker}&isActive=true", Ct))!
            .Items.Select(u => u.Id).ShouldBe([holder]);
        (await factory.AsRoot().GetFromJsonAsync<PagedResult<UserListItem>>($"{Url}?roleId={roleId}", Ct))!
            .Items.Select(u => u.Id).ShouldBe([holder]);

        (await factory.AsRoot().GetAsync($"{Url}?sort=subject", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.AsRoot().GetAsync($"{Url}/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assign_ValidatesRoleScopeAndDates()
    {
        var userId = await factory.UserIdAsync(AccessFactory.NewSubject());
        var roleId = await factory.CreateRoleAsync([PlatformPermissions.Companies.Read]);
        var company = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());

        async Task<Problem> AssignAsync(object body)
        {
            var response = await factory.AsRoot().PostAsJsonAsync($"{Url}/{userId}/assignments", body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
            return await Problem.ReadAsync(response);
        }

        (await AssignAsync(new { roleId = Guid.CreateVersion7(), scopeType = "Tenant" })).Errors!["roleId"].ShouldBe(["Rol bulunamadı."]);
        (await AssignAsync(new { roleId, scopeType = "Galaxy" })).Errors!["scopeType"].ShouldBe(["Kapsam türü geçersiz."]);
        (await AssignAsync(new { roleId, scopeType = "Company" })).Errors!.ShouldContainKey("scopeId");
        (await AssignAsync(new { roleId, scopeType = "Tenant", scopeId = company })).Errors!.ShouldContainKey("scopeId");
        (await AssignAsync(new { roleId, scopeType = "Company", scopeId = Guid.CreateVersion7() })).Errors!["scopeId"].ShouldBe(["Seçilen kapsam kaydı bulunamadı."]);
        (await AssignAsync(new { roleId, scopeType = "Company", scopeId = company, includeSubUnits = true })).Errors!.ShouldContainKey("includeSubUnits");
        var today = AccessFactory.Today;
        (await AssignAsync(new { roleId, scopeType = "Tenant", validFrom = today, validTo = today.AddDays(-1) })).Errors!.ShouldContainKey("validTo");

        await factory.AssignAsync(userId, roleId, "Company", company);
        var duplicate = await factory.AsRoot().PostAsJsonAsync($"{Url}/{userId}/assignments", new { roleId, scopeType = "Company", scopeId = company }, Ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var details = await factory.UserAsync(userId);
        var assignment = details.Assignments.ShouldHaveSingleItem();
        assignment.ScopeName.ShouldBe("Acme");
        assignment.ScopeTypeLabel.ShouldBe("Şirket");
        assignment.RoleName.ShouldBe("Test rolü");
        assignment.IsCurrent.ShouldBeTrue();

        (await factory.AsRoot().PostAsJsonAsync($"{Url}/{Guid.CreateVersion7()}/assignments", new { roleId, scopeType = "Tenant" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeAndRemoveAssignment_AndTheLastAdministratorIsProtected()
    {
        var userId = await factory.UserIdAsync(AccessFactory.NewSubject());
        var roleId = await factory.CreateRoleAsync([PlatformPermissions.Companies.Read]);
        var assignmentId = await factory.AssignAsync(userId, roleId);
        var today = AccessFactory.Today;

        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{userId}/assignments/{assignmentId}", new { validFrom = today, validTo = today.AddDays(-2) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{userId}/assignments/{Guid.CreateVersion7()}", new { validFrom = today }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.AsRoot().DeleteAsync($"{Url}/{userId}/assignments/{assignmentId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.UserAsync(userId)).Assignments.ShouldBeEmpty();
        (await factory.AsRoot().DeleteAsync($"{Url}/{userId}/assignments/{assignmentId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // root holds the only System Administrator assignment of the tenant
        var rootId = await factory.UserIdAsync(AccessFactory.Root);
        var rootAssignment = (await factory.UserAsync(rootId)).Assignments.Single().Id;
        var removeLast = await factory.AsRoot().DeleteAsync($"{Url}/{rootId}/assignments/{rootAssignment}", Ct);
        removeLast.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Problem.ReadAsync(removeLast)).Code.ShouldBe("platform.users.errors.last_administrator");
        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{rootId}/assignments/{rootAssignment}", new { validTo = today.AddDays(-1) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{rootId}/active", new { isActive = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SetActive_DisablesTheIdentity_ButNotOneself()
    {
        var subject = AccessFactory.NewSubject();
        var userId = await factory.UserIdAsync(subject);
        var (admin, adminId) = await factory.UserWithAsync([PlatformPermissions.Users.Read, PlatformPermissions.Users.Manage]);

        (await admin.PutAsJsonAsync($"{Url}/{userId}/active", new { isActive = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        factory.Identity.Enabled[subject].ShouldBeFalse();
        (await factory.UserAsync(userId)).IsActive.ShouldBeFalse();

        var self = await admin.PutAsJsonAsync($"{Url}/{adminId}/active", new { isActive = false }, Ct);
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Problem.ReadAsync(self)).Detail.ShouldBe("Kendi hesabınızı pasifleştiremezsiniz.");

        (await admin.PutAsJsonAsync($"{Url}/{Guid.CreateVersion7()}/active", new { isActive = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.As(AccessFactory.NewSubject()).PutAsJsonAsync($"{Url}/{userId}/active", new { isActive = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PasswordReset_EmailsTheUser()
    {
        var subject = AccessFactory.NewSubject();
        var userId = await factory.UserIdAsync(subject);

        (await factory.AsRoot().PostAsync($"{Url}/{userId}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        factory.Identity.Emails.ShouldContain((subject, IdentityEmail.PasswordReset));

        (await factory.AsRoot().PostAsync($"{Url}/{Guid.CreateVersion7()}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Identity.NextError = IdentityErrors.Unavailable;
        (await factory.AsRoot().PostAsync($"{Url}/{userId}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await factory.As(subject).PostAsync($"{Url}/{userId}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateAdministrator_FromTheMigrator_GivesSystemAdministrator()
    {
        // Another tenant: a second administrator in acme would lift the last-administrator protection other tests rely on
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync("globex", Ct))!;
        var userName = AccessFactory.NewSubject("yonetici.");
        Guid id;
        await using (var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant))
        {
            var result = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new CreateAdministratorCommand(userName, userName + "@example.com", "İlk", "Yönetici"), Ct);
            id = result.Value;
        }

        var admin = (await factory.AsRoot("globex").GetFromJsonAsync<UserDetails>($"{Url}/{id}", Ct))!;
        admin.Assignments.ShouldHaveSingleItem().RoleName.ShouldBe("Sistem Yöneticisi");
        admin.Assignments[0].ScopeType.ShouldBe("Tenant");
        factory.Identity.Emails.ShouldContain(e => e.Email == IdentityEmail.Invitation && factory.Identity.Identities[e.Subject].UserName == userName);
    }

    [Fact]
    public async Task ScopeTypes_AreListedWithLabels()
    {
        var types = (await factory.AsRoot().GetFromJsonAsync<List<Application.Roles.ScopeTypeItem>>("/api/platform/scope-types", Ct))!;

        types.Select(t => t.Code).ShouldBe(["Tenant", "Company", "Branch", "Self"]);
        types.Single(t => t.Code == "Company").ShouldBe(new Application.Roles.ScopeTypeItem("Company", "Şirket", true, false));
    }
}
