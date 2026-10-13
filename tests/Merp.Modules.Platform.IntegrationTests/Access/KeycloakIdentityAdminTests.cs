using System.Net;
using System.Text;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Infrastructure.Identity;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Merp.Modules.Platform.IntegrationTests.Access;

/// <summary>What Keycloak's admin API answers becomes an error of the form, not "unavailable" (D5).</summary>
public sealed class KeycloakIdentityAdminTests
{
    private static readonly NewIdentity Identity = new("mt", "mt@example.com", "Mevlüt", "TIRPAN", "demo");

    [Theory]
    [InlineData("""{"field":"username","errorMessage":"error-invalid-length","params":["username",3,255]}""", "userName", "common.errors.length")]
    [InlineData("""{"field":"firstName","errorMessage":"error-person-name-invalid-character","params":["firstName"]}""", "firstName", IdentityErrors.InvalidPersonNameCode)]
    [InlineData("""{"field":"username","errorMessage":"error-username-invalid-character","params":["username"]}""", "userName", IdentityErrors.InvalidUserNameCode)]
    [InlineData("""{"field":"email","errorMessage":"error-invalid-email","params":["email"]}""", "email", IdentityErrors.InvalidEmailCode)]
    [InlineData("""{"field":"lastName","errorMessage":"error-something-new","params":[]}""", "lastName", IdentityErrors.RejectedCode)]
    public async Task RefusedValue_IsAFieldError(string body, string field, string code)
    {
        var result = await AdminAnswering(HttpStatusCode.BadRequest, body).CreateUserAsync(Identity, TestContext.Current.CancellationToken);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        var error = result.Error.Fields.ShouldHaveSingleItem();
        (error.Field, error.Code).ShouldBe((field, code));
    }

    [Fact]
    public async Task LengthError_CarriesTheLimits()
    {
        var result = await AdminAnswering(HttpStatusCode.BadRequest, """{"field":"username","errorMessage":"error-invalid-length","params":["username",3,255]}""")
            .CreateUserAsync(Identity, TestContext.Current.CancellationToken);

        result.Error.Fields.Single().Args.ShouldBe(new Dictionary<string, object?> { ["minLength"] = 3, ["maxLength"] = 255 });
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"unknown_error"}""")]
    [InlineData(HttpStatusCode.BadRequest, "not json")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    public async Task OtherFailures_AreUnavailable(HttpStatusCode status, string body)
    {
        var result = await AdminAnswering(status, body).CreateUserAsync(Identity, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.Unavailable);
    }

    [Fact]
    public async Task TakenUserName_IsAlreadyExists()
    {
        var result = await AdminAnswering(HttpStatusCode.Conflict, """{"errorMessage":"User exists with same username"}""")
            .CreateUserAsync(Identity, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.AlreadyExists);
    }

    private static KeycloakIdentityAdmin AdminAnswering(HttpStatusCode status, string body)
    {
        var options = Options.Create(new KeycloakAdminOptions { Url = new Uri("http://keycloak.test"), ClientSecret = "secret" });
        var http = new HttpClient(new FakeKeycloak(status, body));
        return new KeycloakIdentityAdmin(http, new KeycloakTokenCache(options, TimeProvider.System), options, NullLogger<KeycloakIdentityAdmin>.Instance);
    }

    /// <summary>Gives a token, then answers user creation with the given response.</summary>
    private sealed class FakeKeycloak(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"t","expires_in":300}""", Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
