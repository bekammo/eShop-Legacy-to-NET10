using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Health;

namespace eShop.Catalog.Api.Catalog;

internal static class CatalogServiceCollectionExtensions
{
    internal static IServiceCollection AddCatalogServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogOptions>()
            .BindConfiguration(CatalogOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(CatalogOptions.SectionName).Get<CatalogOptions>() ?? new CatalogOptions();
        if (options.UseMockData)
        {
            services.AddHostedService<MockModeWarning>();
            return services.AddSingleton<ICatalogService, InMemoryCatalogService>();
        }

        services.AddCatalogDbContext(configuration);
        services.AddMigrateOnStartup();
        services.AddHealthChecks()
            .AddCheck<CatalogDatabaseHealthCheck>(CatalogDatabaseHealthCheck.Name, tags: [HealthCheckEndpoints.ReadinessTag]);
        return services.AddScoped<ICatalogService, CatalogService>();
    }
}
