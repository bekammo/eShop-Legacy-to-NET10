using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.OpenApi;

namespace eShop.Catalog.Api.Http;

internal static class HttpServiceCollectionExtensions
{
    internal static IServiceCollection AddCatalogHttp(this IServiceCollection services)
    {
        // PascalCase is the legacy wire contract; the web default, camelCase, would break existing clients.
        services.ConfigureHttpJsonOptions(static options => options.SerializerOptions.PropertyNamingPolicy = null);

        // Development defaults to true: a parameter that does not bind would throw and become a 500 instead of a 400.
        services.Configure<RouteHandlerOptions>(static options => options.ThrowOnBadRequest = false);

        // Must stay before AddProblemDetails: the first writer that can write is used, so this one replaces
        // ASP.NET Core's own writer.
        services.AddSingleton<IProblemDetailsWriter, ProblemJsonWriter>();
        services.AddProblemDetails();
        services.AddExceptionHandler<AbortedRequestExceptionHandler>();

        services.Configure<KestrelServerOptions>(static options =>
        {
            options.AddServerHeader = false;

            options.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
        });

        services.AddValidation();

        services.AddOpenApi(static options => options.AddSchemaTransformer(static (schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(ProblemDetails)))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                schema.Properties["traceId"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "The request's ID in the W3C trace context form, 00-{trace ID}-{span ID}-{flags}. Its trace ID finds the request in the API's log.",
                };
            }

            return Task.CompletedTask;
        }));
        return services;
    }
}
