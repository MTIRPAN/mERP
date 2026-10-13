using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.Modules.Platform.Application.Access;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merp.Modules.Platform.Infrastructure.Identity;

/// <summary>
/// <c>Keycloak:Admin</c>: the service account (client credentials) of the <c>merp-api</c> client, which holds the
/// realm's <c>manage-users</c> role (deploy/keycloak/merp-realm.json). The secret comes from user secrets or
/// <c>Keycloak__Admin__ClientSecret</c>; without it user administration answers 503.
/// </summary>
public sealed class KeycloakAdminOptions
{
    public const string SectionName = "Keycloak:Admin";

    /// <summary>Keycloak base address, e.g. <c>http://localhost:8080</c>.</summary>
    public Uri? Url { get; set; }

    public string Realm { get; set; } = "merp";

    public string ClientId { get; set; } = "merp-api";

    public string? ClientSecret { get; set; }

    /// <summary>Client and address the e-mailed links return to after the action (the web app).</summary>
    public string WebClientId { get; set; } = "merp-web";

    public Uri? WebUrl { get; set; }

    /// <summary>How long invitation and password links stay valid.</summary>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromDays(3);

    public bool IsConfigured => Url is not null && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Keycloak admin REST API for user administration (D5). Personal data is never logged.</summary>
internal sealed partial class KeycloakIdentityAdmin(
    HttpClient http,
    KeycloakTokenCache tokens,
    IOptions<KeycloakAdminOptions> options,
    ILogger<KeycloakIdentityAdmin> logger) : IIdentityAdmin
{
    private KeycloakAdminOptions Options => options.Value;

    private string UsersUrl => $"{Options.Url!.AbsoluteUri.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(Options.Realm)}/users";

    public Task<Result<string>> CreateUserAsync(NewIdentity identity, CancellationToken cancellationToken = default) =>
        CallAsync<string>(async ct =>
        {
            ArgumentNullException.ThrowIfNull(identity);
            using var response = await SendAsync(HttpMethod.Post, UsersUrl, new KeycloakUser(
                identity.UserName,
                identity.Email,
                identity.FirstName,
                identity.LastName,
                Enabled: true,
                EmailVerified: false,
                new Dictionary<string, string[]> { ["tenant"] = [identity.TenantCode] }), ct);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return IdentityErrors.AlreadyExists;
            }

            // Keycloak refuses a value its user profile does not allow, e.g. a parenthesis in a name
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return await RejectedAsync(response, ct);
            }

            response.EnsureSuccessStatusCode();
            var location = response.Headers.Location ?? throw new HttpRequestException("Keycloak returned no user location.");
            return location.Segments[^1];
        }, cancellationToken);

    public Task<Result> SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken = default) =>
        CallAsync(async ct =>
        {
            using var response = await SendAsync(HttpMethod.Put, $"{UsersUrl}/{Uri.EscapeDataString(subject)}", new { enabled }, ct);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);

    public Task<Result> SendActionsEmailAsync(string subject, IdentityEmail email, CancellationToken cancellationToken = default) =>
        CallAsync(async ct =>
        {
            string[] actions = email == IdentityEmail.Invitation ? ["VERIFY_EMAIL", "UPDATE_PASSWORD"] : ["UPDATE_PASSWORD"];
            var url = $"{UsersUrl}/{Uri.EscapeDataString(subject)}/execute-actions-email?lifespan={(int)Options.LinkLifetime.TotalSeconds}";
            if (Options.WebUrl is not null)
            {
                url += $"&client_id={Uri.EscapeDataString(Options.WebClientId)}&redirect_uri={Uri.EscapeDataString(Options.WebUrl.AbsoluteUri)}";
            }

            using var response = await SendAsync(HttpMethod.Put, url, actions, ct);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync<T>(HttpMethod method, string url, T body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(http, cancellationToken));
        return await http.SendAsync(request, cancellationToken);
    }

    private async Task<Result> CallAsync(Func<CancellationToken, Task> call, CancellationToken cancellationToken)
    {
        var result = await CallAsync<bool>(async ct =>
        {
            await call(ct);
            return true;
        }, cancellationToken);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private async Task<Result<T>> CallAsync<T>(Func<CancellationToken, Task<Result<T>>> call, CancellationToken cancellationToken)
    {
        if (!Options.IsConfigured)
        {
            return IdentityErrors.Unavailable;
        }

        try
        {
            return await call(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception);
            return IdentityErrors.Unavailable;
        }
    }

    /// <summary>
    /// A value Keycloak refused, as a field error of the form: the field and its reason, never the value (personal data).
    /// An answer that is not a field error is treated as Keycloak being unavailable.
    /// </summary>
    private async Task<Error> RejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        KeycloakError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<KeycloakError>(cancellationToken);
        }
        catch (JsonException)
        {
        }

        LogRejected(logger, error?.Field, error?.ErrorMessage);
        var field = error?.Field switch
        {
            "username" => "userName",
            "email" => "email",
            "firstName" => "firstName",
            "lastName" => "lastName",
            _ => null,
        };
        if (field is null)
        {
            return IdentityErrors.Unavailable;
        }

        // e.g. ["username", 3, 255]
        if (error!.ErrorMessage == "error-invalid-length" && error.Params is [_, { ValueKind: JsonValueKind.Number } min, { ValueKind: JsonValueKind.Number } max])
        {
            return Error.Validation([new FieldError(field, "common.errors.length", new Dictionary<string, object?> { ["minLength"] = min.GetInt32(), ["maxLength"] = max.GetInt32() })]);
        }

        var code = error.ErrorMessage switch
        {
            "error-username-invalid-character" => IdentityErrors.InvalidUserNameCode,
            "error-invalid-email" => IdentityErrors.InvalidEmailCode,
            "error-person-name-invalid-character" => IdentityErrors.InvalidPersonNameCode,
            _ => IdentityErrors.RejectedCode,
        };
        return Error.Validation([new FieldError(field, code)]);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak refused field {Field}: {Reason}")]
    private static partial void LogRejected(ILogger logger, string? field, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak admin API call failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    private sealed record KeycloakUser(
        [property: JsonPropertyName("username")] string UserName,
        string Email,
        string FirstName,
        string LastName,
        bool Enabled,
        bool EmailVerified,
        Dictionary<string, string[]> Attributes);

    private sealed record KeycloakError(string? Field, string? ErrorMessage, JsonElement[]? Params);
}

/// <summary>Service account token of the admin client, reused until shortly before it expires.</summary>
internal sealed class KeycloakTokenCache(IOptions<KeycloakAdminOptions> options, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(HttpClient http, CancellationToken cancellationToken)
    {
        if (_token is not null && timeProvider.GetUtcNow() < _expiresAt)
        {
            return _token;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && timeProvider.GetUtcNow() < _expiresAt)
            {
                return _token;
            }

            var settings = options.Value;
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret!,
            });
            using var response = await http.PostAsync(
                $"{settings.Url!.AbsoluteUri.TrimEnd('/')}/realms/{Uri.EscapeDataString(settings.Realm)}/protocol/openid-connect/token",
                form,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new HttpRequestException("Keycloak returned no token.");
            _token = token.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(0, token.ExpiresIn - 30));
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
