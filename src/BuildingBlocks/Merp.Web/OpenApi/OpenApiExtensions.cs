using Merp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Merp.Web.OpenApi;

public static class OpenApiExtensions
{
    public const string SecuritySchemeName = "keycloak";
    public const string DocumentName = "v1";

    /// <summary>OpenAPI document with a Keycloak OAuth2 (authorization code + PKCE) security scheme.</summary>
    public static IServiceCollection AddMerpOpenApi(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"]?.TrimEnd('/') ?? "http://localhost:8080/realms/merp";
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo { Title = "mERP API", Version = DocumentName };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(authority + "/protocol/openid-connect/auth"),
                            TokenUrl = new Uri(authority + "/protocol/openid-connect/token"),
                            Scopes = new Dictionary<string, string> { ["openid"] = "OpenID", ["profile"] = "Profile" },
                        },
                    },
                };
                return Task.CompletedTask;
            });

            // Secured operations reference the scheme and name their permission.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SecuritySchemeName, context.Document)] = ["openid"] });
                var permissions = metadata.OfType<RequiredPermission>().Select(p => p.Permission).ToList();
                if (permissions.Count > 0)
                {
                    operation.Description = string.Join(' ', new[] { operation.Description, $"Requires permission: {string.Join(", ", permissions)}." }.Where(s => !string.IsNullOrWhiteSpace(s)));
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary><c>/openapi/v1.json</c> and the Scalar reference at <c>/scalar</c> with Keycloak PKCE sign-in.</summary>
    public static IEndpointRouteBuilder MapMerpApiDocs(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var clientId = configuration["Authentication:WebClientId"] ?? "merp-web";
        endpoints.MapOpenApi().AllowAnonymous();
        endpoints.MapScalarApiReference(options => options
            .WithTitle("mERP API")
            .AddPreferredSecuritySchemes([SecuritySchemeName])
            .AddAuthorizationCodeFlow(SecuritySchemeName, flow => flow
                .WithClientId(clientId)
                .WithPkce(Pkce.Sha256)
                .WithSelectedScopes(["openid", "profile"])))
            .AllowAnonymous();
        return endpoints;
    }
}
