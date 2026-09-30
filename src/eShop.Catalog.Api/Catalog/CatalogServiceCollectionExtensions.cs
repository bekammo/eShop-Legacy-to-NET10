using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Health;

namespace eShop.Catalog.Api.Catalog;

internal static class CatalogServiceCollectionExtensions
{
    // Registers the catalog service that Catalog:UseMockData chooses, in place of the legacy Autofac
    // ApplicationModule (ADR-0017):
    // - In mock mode, one InMemoryCatalogService for the whole app, and nothing of the database: no DbContext, no
    //   migration at startup, no readiness check of the database, and no connection string needed.
    // - Otherwise, a CatalogService per scope over CatalogDbContext, with migrate-on-startup and the readiness check.
    internal static IServiceCollection AddCatalogServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogOptions>()
            .BindConfiguration(CatalogOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The mode decides what is registered, so it is read now, before the container exists. A malformed value
        // stops the host here, with a message that names the setting.
        var options = configuration.GetSection(CatalogOptions.SectionName).Get<CatalogOptions>() ?? new CatalogOptions();
        if (options.UseMockData)
        {
            return services.AddSingleton<ICatalogService, InMemoryCatalogService>();
        }

        services.AddCatalogDbContext(configuration);
        services.AddMigrateOnStartup();
        services.AddHealthChecks()
            .AddCheck<CatalogDatabaseHealthCheck>(CatalogDatabaseHealthCheck.Name, tags: [HealthCheckEndpoints.ReadinessTag]);
        return services.AddScoped<ICatalogService, CatalogService>();
    }
}
