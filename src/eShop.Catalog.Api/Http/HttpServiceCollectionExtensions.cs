using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace eShop.Catalog.Api.Http;

internal static class HttpServiceCollectionExtensions
{
    // What every endpoint shares: JSON, validation and the OpenAPI document (ADR-0020), and errors as problem details
    // (ADR-0021).
    internal static IServiceCollection AddCatalogHttp(this IServiceCollection services)
    {
        // PascalCase, as Web API 2 wrote it with Newtonsoft's defaults (ADR-0002). The other web defaults stay.
        services.ConfigureHttpJsonOptions(static options => options.SerializerOptions.PropertyNamingPolicy = null);

        // A parameter that does not bind is a 400 in every environment. In Development the default is to throw, and
        // the exception handler would make it a 500.
        services.Configure<RouteHandlerOptions>(static options => options.ThrowOnBadRequest = false);

        // Before AddProblemDetails, so that it comes before ASP.NET Core's own writer, which is then never used.
        services.AddSingleton<IProblemDetailsWriter, ProblemJsonWriter>();
        services.AddProblemDetails();

        // The Server header names the stack (audit D19).
        services.Configure<KestrelServerOptions>(static options => options.AddServerHeader = false);

        services.AddValidation();
        services.AddOpenApi();
        return services;
    }
}
