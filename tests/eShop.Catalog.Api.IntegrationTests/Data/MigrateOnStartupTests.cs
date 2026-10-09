using System.Net;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace eShop.Catalog.Api.IntegrationTests.Data;

[Trait("Category", "Docker")]
public sealed class MigrateOnStartupTests(SqlServerFixture sqlServer)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Development_host_migrates_and_seeds_its_database_before_the_server_starts()
    {
        await using var factory = new CatalogApiFactory(sqlServer);
        await factory.UseNewDatabaseAsync();
        var probe = new StartupProbe(factory.ConnectionString);
        var host = Host(factory, Environments.Development, migrateOnStartup: "true", services => services.AddHostedService(_ => probe));

        _ = host.Services;

        Assert.NotNull(probe.PendingMigrationsWhenServicesStart);
        Assert.Empty(probe.PendingMigrationsWhenServicesStart);
        Assert.Equal(LegacySeedData.Items, await CatalogDatabase.ItemsAsync(factory.ConnectionString, CancellationToken));
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/health/ready", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Development", "false")]
    [InlineData("Testing", null)]
    [InlineData("Production", null)]
    public async Task Host_leaves_the_database_alone_with_migrate_on_startup_off(string environment, string? migrateOnStartup)
    {
        await using var factory = new CatalogApiFactory(sqlServer);
        await factory.UseNewDatabaseAsync();
        var host = Host(factory, environment, migrateOnStartup);

        _ = host.Services;

        Assert.False(await DatabaseExistsAsync(factory.ConnectionString));
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task Host_outside_Development_refuses_to_start_with_migrate_on_startup(string environment)
    {
        await using var factory = new CatalogApiFactory(sqlServer);
        await factory.UseNewDatabaseAsync();
        var host = Host(factory, environment, migrateOnStartup: "true");

        var exception = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains("Database:MigrateOnStartup is allowed only in the Development environment", exception.Message, StringComparison.Ordinal);
        Assert.False(await DatabaseExistsAsync(factory.ConnectionString));
    }

    [Fact]
    public async Task Host_does_not_start_with_a_malformed_migrate_on_startup()
    {
        await using var factory = new CatalogApiFactory(sqlServer);
        await factory.UseNewDatabaseAsync();
        var host = Host(factory, Environments.Development, migrateOnStartup: "yes");

        var exception = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains("'Database:MigrateOnStartup'", exception.Message, StringComparison.Ordinal);
        Assert.False(await DatabaseExistsAsync(factory.ConnectionString));
    }

    private static WebApplicationFactory<Program> Host(
        CatalogApiFactory factory, string environment, string? migrateOnStartup, Action<IServiceCollection>? services = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (migrateOnStartup is not null)
            {
                builder.UseSetting("Database:MigrateOnStartup", migrateOnStartup);
            }

            if (services is not null)
            {
                builder.ConfigureTestServices(services);
            }
        });

    private static async Task<bool> DatabaseExistsAsync(string connectionString)
    {
        await using var context = CatalogDatabase.CreateContext(connectionString);
        return await context.Database.CanConnectAsync(CancellationToken);
    }

    // Checks in StartingAsync, registered after the app's services: every StartingAsync runs before any StartAsync,
    // the server's included. Checking any later would pass even if the migration ran after the server started.
    private sealed class StartupProbe(string connectionString) : IHostedLifecycleService
    {
        public IReadOnlyList<string>? PendingMigrationsWhenServicesStart { get; private set; }

        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using var context = CatalogDatabase.CreateContext(connectionString);
            PendingMigrationsWhenServicesStart = [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
