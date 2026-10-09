using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace eShop.Catalog.Api.Authorization;

internal static class AuthorizationServiceCollectionExtensions
{
    // JWT bearer tokens, and the catalog:write policy (ADR-0034). The Bearer scheme reads its settings from the
    // Authentication:Schemes:Bearer section, where dotnet user-jwts writes its issuer and audiences
    // (appsettings.Development.json) and its signing key (user secrets). Where the section names no signing key and no
    // authority, no token is valid, and every write is a 401.
    internal static IServiceCollection AddCatalogAuthorization(this IServiceCollection services)
    {
        services.AddAuthentication().AddJwtBearer();
        services.AddAuthorizationBuilder()
            .AddPolicy(CatalogScopes.Write, static policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(static context => HasScope(context.User, CatalogScopes.Write)));

        // The Bearer scheme in the document that AddCatalogHttp adds, and on each operation whose endpoint requires a
        // policy, a requirement that names the policy, which is its scope. Swagger UI then asks for a token, and sends it
        // with those operations.
        services.Configure<OpenApiOptions>("v1", static options => options
            .AddDocumentTransformer(static (document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "An access token, a JWT, with the scope that the operation names.",
                };
                return Task.CompletedTask;
            })
            .AddOperationTransformer(static (operation, context, _) =>
            {
                List<string> policies = [.. context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Select(static data => data.Policy).OfType<string>()];
                if (policies.Count > 0)
                {
                    operation.Security =
                    [
                        new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, context.Document)] = policies },
                    ];
                }

                return Task.CompletedTask;
            }));
        return services;
    }

    // A token lists its scopes in scope claims: one claim for each scope, as dotnet user-jwts writes them, or one claim
    // that separates them with spaces, as RFC 9068 does for the access tokens of an OAuth 2.0 authorization server.
    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope").Any(claim => claim.Value.Split(' ').Contains(scope));
}
