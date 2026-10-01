using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.UnitTests.Catalog;

// What AddCatalogServices registers in each mode (ADR-0017), without a host or a database.
public sealed class CatalogServiceCollectionExtensionsTests
{
    private const string ConnectionString = "Data Source=unused;Initial Catalog=unused";

    [Fact]
    public void Database_mode_registers_a_scoped_catalog_service_and_the_database()
    {
        var services = Register(useMockData: null, ConnectionString);

        AssertCatalogService<CatalogService>(services, ServiceLifetime.Scoped);
        Assert.Contains(services, static service => service.ServiceType == typeof(CatalogDbContext));
        Assert.Contains(services, static service => service.ImplementationType == typeof(MigrateOnStartupService));
        Assert.DoesNotContain(services, static service => service.ImplementationType == typeof(MockModeWarning));
        Assert.Equal(["catalog-database"], HealthChecks(services));
    }

    // A mock-mode host needs no connection string, and reads no Database section. Its one hosted service warns that
    // it runs in mock mode (ADR-0019).
    [Fact]
    public void Mock_mode_registers_one_in_memory_catalog_and_nothing_of_the_database()
    {
        var services = Register(useMockData: "true", connectionString: null);

        AssertCatalogService<InMemoryCatalogService>(services, ServiceLifetime.Singleton);
        Assert.DoesNotContain(services, static service => service.ServiceType == typeof(CatalogDbContext));
        Assert.Equal([typeof(MockModeWarning)],
            services.Where(static service => service.ServiceType == typeof(IHostedService)).Select(static service => service.ImplementationType));
        Assert.Empty(HealthChecks(services));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    public void Explicit_false_is_database_mode(string useMockData) =>
        AssertCatalogService<CatalogService>(Register(useMockData, ConnectionString), ServiceLifetime.Scoped);

    [Fact]
    public void Database_mode_still_needs_a_connection_string()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Register(useMockData: "false", connectionString: null));

        Assert.Contains("'ConnectionStrings:CatalogDb' is not set", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_use_mock_data_is_refused_while_registering()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Register(useMockData: "yes", ConnectionString));

        Assert.Contains("'Catalog:UseMockData'", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceCollection Register(string? useMockData, string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:UseMockData"] = useMockData,
                ["ConnectionStrings:CatalogDb"] = connectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddCatalogServices(configuration);
        return services;
    }

    private static void AssertCatalogService<TImplementation>(ServiceCollection services, ServiceLifetime lifetime)
    {
        var catalogService = Assert.Single(services, static service => service.ServiceType == typeof(ICatalogService));
        Assert.Equal(typeof(TImplementation), catalogService.ImplementationType);
        Assert.Equal(lifetime, catalogService.Lifetime);
    }

    // The names of the registered health checks.
    private static IReadOnlyList<string> HealthChecks(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Select(static check => check.Name)];
    }
}
