using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Web.Endpoints;

namespace Merp.Modules.Platform.IntegrationTests.Access;

public sealed class RoleEndpointsTests(AccessFactory factory) : IClassFixture<AccessFactory>
{
    private const string Url = "/api/platform/roles";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SystemRoles_AreSeeded_WithTheirDefaultPermissions()
    {
        var roles = (await factory.AsRoot().GetFromJsonAsync<PagedResult<RoleListItem>>($"{Url}?pageSize=200&sort=code", Ct))!;

        roles.Items.Where(r => r.IsSystem).Select(r => r.Code).ShouldBe(
            ["EMPLOYEE", "HR-MANAGER", "HR-SPECIALIST", "INTERVIEWER", "MANAGER", "RECRUITER", "SYSTEM-ADMIN"]);
        var admin = roles.Items.Single(r => r.Code == "SYSTEM-ADMIN");
        admin.Name.ShouldBe("Sistem Yöneticisi");
        admin.UserCount.ShouldBe(1);

        var details = (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"{Url}/{admin.Id}", Ct))!;
        details.Permissions.ShouldContain(PlatformPermissions.Roles.Manage);
        details.Permissions.ShouldAllBe(p => p.StartsWith("platform.", StringComparison.Ordinal));
        var recruiter = await factory.SystemRoleAsync("recruiter");
        (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"{Url}/{recruiter.Id}", Ct))!.Permissions.ShouldBe(["recruitment.cv.analyze", "recruitment.openings.manage", "recruitment.openings.read", "recruitment.requisitions.manage", "recruitment.requisitions.read", "recruitment.settings.manage", "recruitment.settings.read"], ignoreOrder: true);
    }

    [Fact]
    public async Task Create_ThenGet_ReturnsTheRole()
    {
        var code = "bordro-" + Guid.NewGuid().ToString("N")[..6];
        var response = await factory.AsRoot().PostAsJsonAsync(
            Url,
            new { code, name = new Dictionary<string, string> { ["tr-TR"] = "Bordro Uzmanı" }, description = "Bordro işleri", permissions = new[] { PlatformPermissions.Companies.Read } },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        var role = (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"{Url}/{id}", Ct))!;
        role.Code.ShouldBe(code.ToUpperInvariant());
        role.Name["tr-TR"].ShouldBe("Bordro Uzmanı");
        role.IsSystem.ShouldBeFalse();
        role.Permissions.ShouldBe([PlatformPermissions.Companies.Read]);

        var found = (await factory.AsRoot().GetFromJsonAsync<PagedResult<RoleListItem>>($"{Url}?search=bordro uzm", Ct))!;
        found.Items.Select(r => r.Id).ShouldContain(id);
    }

    [Fact]
    public async Task Create_WithInvalidInput_Returns400()
    {
        var unknown = await factory.AsRoot().PostAsJsonAsync(
            Url,
            new { code = "R1", name = new Dictionary<string, string> { ["tr-TR"] = "Rol" }, permissions = new[] { "platform.rockets.launch" } },
            Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(unknown)).Errors!["permissions"].ShouldBe(["Tanımsız yetki: platform.rockets.launch."]);

        var invalid = await factory.AsRoot().PostAsJsonAsync(Url, new { code = "rol 1", name = new Dictionary<string, string>(), permissions = (string[]?)null }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["code", "name", "permissions"], ignoreOrder: true);

        var duplicate = await factory.AsRoot().PostAsJsonAsync(
            Url,
            new { code = "system-admin", name = new Dictionary<string, string> { ["tr-TR"] = "Kopya" }, permissions = Array.Empty<string>() },
            Ct);
        (await Problem.ReadAsync(duplicate)).Errors!.ShouldContainKey("code");

        (await factory.AsRoot().GetAsync($"{Url}?sort=secret", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RoleAdministration_NeedsThePermission_WithTheTenantScope()
    {
        var company = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var (scoped, _) = await factory.UserWithAsync([PlatformPermissions.Roles.Read, PlatformPermissions.Roles.Manage], "Company", company);
        var (reader, _) = await factory.UserWithAsync([PlatformPermissions.Roles.Read]);
        var body = new { code = "X1", name = new Dictionary<string, string> { ["tr-TR"] = "X" }, permissions = Array.Empty<string>() };

        (await scoped.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.PostAsJsonAsync(Url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.GetAsync("/api/platform/permissions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync(Url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.As(AccessFactory.NewSubject()).GetAsync("/api/platform/permissions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_ReplacesThePermissions_AndDetectsConcurrentChanges()
    {
        var id = await factory.CreateRoleAsync([PlatformPermissions.Companies.Read]);
        var role = (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"{Url}/{id}", Ct))!;
        var body = new
        {
            version = role.Version,
            name = new Dictionary<string, string> { ["tr-TR"] = "Yeni ad" },
            description = "Açıklama",
            permissions = new[] { PlatformPermissions.Branches.Read, PlatformPermissions.Branches.Manage },
        };

        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{id}", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var updated = (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"{Url}/{id}", Ct))!;
        updated.Name["tr-TR"].ShouldBe("Yeni ad");
        updated.Description.ShouldBe("Açıklama");
        updated.Permissions.ShouldBe([PlatformPermissions.Branches.Manage, PlatformPermissions.Branches.Read]);

        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{id}", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{Guid.CreateVersion7()}", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.AsRoot().PutAsJsonAsync($"{Url}/{id}", body with { name = new Dictionary<string, string>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_OnlyCustomRolesNobodyHolds()
    {
        var unused = await factory.CreateRoleAsync([]);
        (await factory.AsRoot().DeleteAsync($"{Url}/{unused}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.AsRoot().GetAsync($"{Url}/{unused}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var held = await factory.CreateRoleAsync([]);
        await factory.AssignAsync(await factory.UserIdAsync(AccessFactory.NewSubject()), held);
        var inUse = await factory.AsRoot().DeleteAsync($"{Url}/{held}", Ct);
        inUse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Problem.ReadAsync(inUse)).Code.ShouldBe("platform.roles.errors.in_use");

        var system = await factory.SystemRoleAsync("interviewer");
        (await factory.AsRoot().DeleteAsync($"{Url}/{system.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reset_RestoresASystemRole()
    {
        var recruiter = await factory.SystemRoleAsync("recruiter");
        var role = (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"{Url}/{recruiter.Id}", Ct))!;
        await factory.AsRoot().PutAsJsonAsync(
            $"{Url}/{recruiter.Id}",
            new { version = role.Version, name = new Dictionary<string, string> { ["tr-TR"] = "Değişti" }, permissions = new[] { PlatformPermissions.Companies.Read } },
            Ct);

        (await factory.AsRoot().PostAsync($"{Url}/{recruiter.Id}/reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reset = (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"{Url}/{recruiter.Id}", Ct))!;
        reset.Name["tr-TR"].ShouldBe("İşe Alım Uzmanı");
        reset.Permissions.ShouldBe(["recruitment.cv.analyze", "recruitment.openings.manage", "recruitment.openings.read", "recruitment.requisitions.manage", "recruitment.requisitions.read", "recruitment.settings.manage", "recruitment.settings.read"], ignoreOrder: true);

        var custom = await factory.CreateRoleAsync([]);
        (await factory.AsRoot().PostAsync($"{Url}/{custom}/reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.AsRoot().PostAsync($"{Url}/{Guid.CreateVersion7()}/reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PermissionTree_GroupsByModuleAndResource_WithTurkishLabels()
    {
        var tree = (await factory.AsRoot().GetFromJsonAsync<List<PermissionModuleNode>>("/api/platform/permissions", Ct))!;

        tree.Select(m => m.Code).ShouldBe(["hr", "platform", "recruitment"]);
        var companies = tree[1].Resources.Single(r => r.Code == "platform.companies");
        companies.Label.ShouldBe("Şirketler");
        companies.Permissions.ShouldBe(
        [
            new PermissionNode("platform.companies.manage", "Oluşturma ve düzenleme"),
            new PermissionNode("platform.companies.read", "Görüntüleme"),
        ]);
        tree[0].Label.ShouldBe("İnsan kaynakları");
        tree[2].Label.ShouldBe("İşe alım");
        tree.SelectMany(m => m.Resources).SelectMany(r => r.Permissions).ShouldAllBe(p => !p.Label.StartsWith("permissions.", StringComparison.Ordinal));
    }
}
