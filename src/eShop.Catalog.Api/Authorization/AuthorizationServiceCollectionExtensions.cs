using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace eShop.Catalog.Api.Authorization;

internal static class AuthorizationServiceCollectionExtensions
{
    internal static IServiceCollection AddCatalogAuthorization(this IServiceCollection services)
    {
        services.AddAuthentication().AddJwtBearer();
        services.AddAuthorizationBuilder()
            .AddPolicy(CatalogScopes.Write, static policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(static context => HasScope(context.User, CatalogScopes.Write)));

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

    // Tokens carry scopes as one claim per scope (dotnet user-jwts) or as one space-separated claim (RFC 9068), and
    // HasScope must accept both.
    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope").Any(claim => claim.Value.Split(' ').Contains(scope));
}
