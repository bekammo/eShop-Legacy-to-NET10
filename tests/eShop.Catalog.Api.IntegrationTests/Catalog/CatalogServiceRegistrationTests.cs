using System.Net;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.IntegrationTests.Logging;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace eShop.Catalog.Api.IntegrationTests.Catalog;

// AddCatalogServices in the host (ADR-0017): Catalog:UseMockData chooses the catalog service, mock mode needs no
// database, and the container validates scopes in every environment.
[Trait("Category", "Docker")]
public sealed class CatalogServiceRegistrationTests(CatalogApiFactory factory, SqlServerFixture sqlServer) : IClassFixture<CatalogApiFactory>
{
    // The name of the warning event: the name of its [LoggerMessage] method.
    private const string MockModeEvent = "MockModeIsOn";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Database_mode_gives_each_scope_its_own_catalog_service()
    {
        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();

        var service = first.ServiceProvider.GetRequiredService<ICatalogService>();

        Assert.IsType<CatalogService>(service);
        Assert.Same(service, first.ServiceProvider.GetRequiredService<ICatalogService>());
        Assert.NotSame(service, second.ServiceProvider.GetRequiredService<ICatalogService>());
        Assert.Equal(LegacySeedData.Items, (await service.GetCatalogItemsPaginatedAsync(12, 0, CancellationToken)).Data.Select(LegacySeedData.Item));
    }

    // Settings that stop a host in database mode, and that mock mode does not read: a blank connection string, and
    // migrate-on-startup, which is allowed only in Development. The factory is never initialized, so it has no database
    // either.
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task Mock_mode_serves_the_sample_data_without_a_database(string environment)
    {
        await using var withoutDatabase = new CatalogApiFactory(sqlServer);
        await using var host = withoutDatabase.WithWebHostBuilder(builder => builder
            .UseEnvironment(environment)
            .UseSetting("Catalog:UseMockData", "true")
            .UseSetting("ConnectionStrings:CatalogDb", "")
            .UseSetting("Database:MigrateOnStartup", "true"));

        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        Assert.IsType<InMemoryCatalogService>(service);
        Assert.Same(service, host.Services.GetRequiredService<ICatalogService>());
        Assert.Null(scope.ServiceProvider.GetService<CatalogDbContext>());
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), static hosted => hosted is MigrateOnStartupService);
        Assert.Equal(LegacySeedData.Items, (await service.GetCatalogItemsPaginatedAsync(12, 0, CancellationToken)).Data.Select(LegacySeedData.Item));

        using var client = host.CreateClient();
        using var response = await client.GetAsync("/health/ready", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    // A host in mock mode is ready at once, so it says what it serves when it starts (ADR-0019).
    [Fact]
    public async Task Mock_mode_warns_at_startup_that_changes_are_lost()
    {
        var logFile = Path.Combine(Path.GetDirectoryName(factory.LogFilePath)!, $"{Guid.NewGuid():N}.log");
        await using (var host = factory.WithWebHostBuilder(builder =>
            CatalogApiFactory.UseLogFile(builder.UseSetting("Catalog:UseMockData", "true"), logFile)))
        {
            _ = host.Services;
        }

        var warning = Assert.Single(LogFile.Events(logFile), static logEvent => LogFile.EventName(logEvent) == MockModeEvent);
        Assert.Equal("Warning", LogFile.String(warning, "@l"));
        Assert.StartsWith("Mock mode is on (Catalog:UseMockData)", LogFile.String(warning, "@mt"), StringComparison.Ordinal);
        Assert.Contains("every change is lost", LogFile.String(warning, "@mt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Database_mode_does_not_warn_about_mock_mode()
    {
        _ = factory.Services;

        Assert.DoesNotContain(LogFile.Events(factory.LogFilePath), static logEvent => LogFile.EventName(logEvent) == MockModeEvent);
    }

    [Fact]
    public void Host_does_not_start_with_a_malformed_use_mock_data()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Catalog:UseMockData", "yes"));

        var exception = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains("'Catalog:UseMockData'", exception.Message, StringComparison.Ordinal);
    }

    // ASP.NET Core validates scopes only in Development by default. The factory's host runs in Testing.
    [Fact]
    public void Scoped_services_cannot_be_resolved_from_the_root_provider()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services.GetRequiredService<ICatalogService>());

        Assert.Contains("from root provider", exception.Message, StringComparison.Ordinal);
    }

    // The container checks every registration when it is built, so a singleton that would hold a scoped service
    // stops the host at startup, before anything resolves it.
    [Fact]
    public void Host_does_not_start_with_a_singleton_that_holds_a_scoped_service()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(static services => services.AddSingleton<CatalogServiceHolder>()));

        var exception = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains($"Cannot consume scoped service '{typeof(ICatalogService)}'", exception.Message, StringComparison.Ordinal);
    }

    private sealed class CatalogServiceHolder(ICatalogService service)
    {
        public ICatalogService Service { get; } = service;
    }
}
