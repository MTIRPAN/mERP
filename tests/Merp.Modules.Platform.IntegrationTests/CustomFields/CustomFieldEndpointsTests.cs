using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.Modules.Platform.IntegrationTests.Access;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.SharedKernel.CustomFields;
using Merp.Web.Endpoints;

namespace Merp.Modules.Platform.IntegrationTests.CustomFields;

/// <summary>Custom fields (PLAN P6, D12, D30). Fields that change company rules are defined in globex only.</summary>
public sealed class CustomFieldEndpointsTests(AccessFactory factory) : IClassFixture<AccessFactory>
{
    private const string Url = "/api/platform/custom-fields";
    private const string Tenant = "globex";
    private const string Companies = "/api/platform/companies";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Root => factory.AsRoot(Tenant);

    private static object Field(string code, string type, string label, bool required = false, object? options = null, Guid? companyId = null, decimal? min = null, decimal? max = null) => new
    {
        entityType = PlatformCustomFieldEntities.Company,
        code,
        type,
        companyId,
        label = new Dictionary<string, string> { ["tr-TR"] = label },
        required,
        options,
        min,
        max,
        order = 10,
    };

    private async Task<Guid> DefineAsync(object field, HttpClient? client = null)
    {
        var response = await (client ?? Root).PostAsJsonAsync($"{Url}/definitions", field, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
    }

    private static Dictionary<string, object?> CompanyBody(object? customFields)
    {
        var body = JsonSerializer.SerializeToElement(OrganizationFactory.CompanyBody()).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value);
        body["customFields"] = customFields;
        return body;
    }

    /// <summary>
    /// PLAN P6 acceptance: "Ayakkabı numarası (sayı, zorunlu)" appears on the form, an empty value gets a Turkish
    /// error, and the list can be filtered by it.
    /// </summary>
    [Fact]
    public async Task ShoeSize_IsOnTheForm_Required_AndFilterable()
    {
        await DefineAsync(Field("shoe_size", "Number", "Ayakkabı numarası", required: true, min: 30, max: 50));

        var form = (await Root.GetFromJsonAsync<List<CustomFieldFormItem>>($"{Url}/form?entityType=platform.company", Ct))!;
        var shoeSize = form.ShouldHaveSingleItem();
        (shoeSize.Code, shoeSize.Label, shoeSize.Type, shoeSize.Required).ShouldBe(("shoe_size", "Ayakkabı numarası", CustomFieldType.Number, true));

        var missing = await Root.PostAsJsonAsync(Companies, CompanyBody(null), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(missing)).Errors!["customFields.shoe_size"].ShouldBe(["Bu alan zorunludur."]);
        var outOfRange = await Root.PostAsJsonAsync(Companies, CompanyBody(new { shoe_size = 60 }), Ct);
        (await Problem.ReadAsync(outOfRange)).Errors!["customFields.shoe_size"].ShouldBe(["30 ile 50 arasında olmalıdır."]);

        var created = await Root.PostAsJsonAsync(Companies, CompanyBody(new { shoe_size = 42 }), Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        await Root.PostAsJsonAsync(Companies, CompanyBody(new { shoe_size = 38 }), Ct);

        var details = (await Root.GetFromJsonAsync<CompanyDetails>($"{Companies}/{id}", Ct))!;
        details.CustomFields["shoe_size"].GetDecimal().ShouldBe(42);

        var filtered = (await Root.GetFromJsonAsync<PagedResult<CompanyListItem>>($"{Companies}?customFields={Uri.EscapeDataString("{\"shoe_size\":42}")}", Ct))!;
        filtered.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
        filtered.Items[0].CustomFields["shoe_size"].GetDecimal().ShouldBe(42);
        (await Root.GetFromJsonAsync<PagedResult<CompanyListItem>>($"{Companies}?customFields={Uri.EscapeDataString("{\"shoe_size\":41}")}", Ct))!.TotalCount.ShouldBe(0);
        (await Root.GetAsync($"{Companies}?customFields=not-json", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CompanyFields_ApplyToTheirCompany_SwitchedOffFieldsKeepTheirValues()
    {
        var company = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var other = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var fieldId = await DefineAsync(
            Field("tier", "Select", "Seviye", options: new[] { new { code = "gold", label = new Dictionary<string, string> { ["tr-TR"] = "Altın" } } }, companyId: company),
            factory.AsRoot());

        (await factory.AsRoot().GetFromJsonAsync<List<CustomFieldFormItem>>($"{Url}/form?entityType=platform.company&companyId={company}", Ct))!
            .Select(f => f.Code).ShouldBe(["tier"]);
        (await factory.AsRoot().GetFromJsonAsync<List<CustomFieldFormItem>>($"{Url}/form?entityType=platform.company&companyId={other}", Ct))!
            .ShouldBeEmpty();

        await UpdateCompanyAsync(company, new { tier = "gold" }, HttpStatusCode.NoContent);
        await UpdateCompanyAsync(company, new { tier = "silver" }, HttpStatusCode.BadRequest);
        await UpdateCompanyAsync(other, new { tier = "gold" }, HttpStatusCode.BadRequest);

        // Switched off: no longer on the form or accepted, but the stored value stays
        var definition = (await factory.AsRoot().GetFromJsonAsync<List<CustomFieldDefinitionItem>>($"{Url}/definitions?entityType=platform.company", Ct))!
            .Single(d => d.Id == fieldId);
        var off = await factory.AsRoot().PutAsJsonAsync(
            $"{Url}/definitions/{fieldId}",
            new { version = definition.Version, label = definition.Label, required = false, options = definition.Options, order = 10, isActive = false },
            Ct);
        off.StatusCode.ShouldBe(HttpStatusCode.NoContent, await off.Content.ReadAsStringAsync(Ct));
        await UpdateCompanyAsync(company, null, HttpStatusCode.NoContent);
        (await factory.AsRoot().GetFromJsonAsync<CompanyDetails>($"{Companies}/{company}", Ct))!.CustomFields["tier"].GetString().ShouldBe("gold");
    }

    [Fact]
    public async Task InvalidDefinitions_AndValues_Return400()
    {
        async Task<Dictionary<string, string[]>> ErrorsAsync(object field) =>
            (await Problem.ReadAsync(await factory.AsRoot().PostAsJsonAsync($"{Url}/definitions", field, Ct))).Errors!;

        (await ErrorsAsync(Field("Bad Code", "Text", "Kötü"))).ShouldContainKey("code");
        (await ErrorsAsync(Field("size", "Select", "Beden"))).ShouldContainKey("options");
        (await ErrorsAsync(Field("note", "Text", "Not", companyId: Guid.NewGuid()))).ShouldContainKey("companyId");
        (await ErrorsAsync(new { entityType = "platform.rocket", code = "x", type = "Text", label = new Dictionary<string, string> { ["tr-TR"] = "X" } })).ShouldContainKey("entityType");
        (await ErrorsAsync(new { entityType = "platform.company", code = "x", type = "Text", label = new Dictionary<string, string> { ["en-US"] = "X" } })).ShouldContainKey("label");

        await DefineAsync(Field("owner", "User", "Sorumlu"), factory.AsRoot());
        (await ErrorsAsync(Field("owner", "Text", "Tekrar"))).ShouldContainKey("code");

        var company = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var unknown = await UpdateCompanyAsync(company, new { owner = Guid.NewGuid(), nope = 1 }, HttpStatusCode.BadRequest, factory.AsRoot());
        (await Problem.ReadAsync(unknown)).Errors!.Keys.ShouldBe(["customFields.nope", "customFields.owner"], ignoreOrder: true);
        var rootId = await factory.UserIdAsync(AccessFactory.Root);
        await UpdateCompanyAsync(company, new { owner = rootId }, HttpStatusCode.NoContent, factory.AsRoot());
    }

    [Fact]
    public async Task Definitions_NeedThePermission_WithTheTenantScope_TheFormOnlyASignIn()
    {
        var (reader, _) = await factory.UserWithAsync([PlatformPermissions.Companies.Read]);
        (await reader.GetAsync($"{Url}/definitions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"{Url}/form?entityType=platform.company", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var companyId = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var (companyScoped, _) = await factory.UserWithAsync([PlatformPermissions.CustomFields.Read, PlatformPermissions.CustomFields.Manage], "Company", companyId);
        var response = await companyScoped.PostAsJsonAsync($"{Url}/definitions", Field("note", "Text", "Not"), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.custom_fields.errors.tenant_scope_required");
        (await factory.CreateClient().GetAsync($"{Url}/form?entityType=platform.company", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> UpdateCompanyAsync(Guid id, object? customFields, HttpStatusCode expected, HttpClient? client = null)
    {
        client ??= factory.AsRoot();
        var company = (await client.GetFromJsonAsync<CompanyDetails>($"{Companies}/{id}", Ct))!;
        var body = new
        {
            version = company.Version,
            title = company.Title,
            shortName = company.ShortName,
            taxOffice = company.TaxOffice,
            taxNumber = company.TaxNumber,
            address = new { line1 = company.Address!.Line1, provinceCode = company.Address.ProvinceCode, districtCode = company.Address.DistrictCode },
            culture = company.Culture,
            timeZoneId = company.TimeZoneId,
            isActive = company.IsActive,
            customFields,
        };
        var response = await client.PutAsJsonAsync($"{Companies}/{id}", body, Ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return response;
    }
}
