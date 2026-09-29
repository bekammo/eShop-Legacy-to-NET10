using eShop.Catalog.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests;

// Hosts the API in memory for the integration tests (ADR-0007). Each test class gets its own
// instance through IClassFixture<CatalogApiFactory>, and with it a database of its own in the shared
// SQL Server container, migrated before the class's first test.
public sealed class CatalogApiFactory(SqlServerFixture sqlServer) : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Not Development: user secrets and Development-only features stay off unless a test opts in.
    public const string EnvironmentName = "Testing";

    // The Testing environment has no connection string of its own (ADR-0009): this class's database.
    public string ConnectionString { get; } = sqlServer.NewDatabase("catalog");

    public async ValueTask InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    // WebApplicationFactory passes host settings to Program as command-line arguments, which
    // CreateBuilder reads first, so Program.cs sees them while it registers services; they also
    // override environment variables. ConfigureAppConfiguration would apply only after that.
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(EnvironmentName)
            .UseSetting("ConnectionStrings:CatalogDb", ConnectionString);
}
