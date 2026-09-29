using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace eShop.Catalog.Api.IntegrationTests;

// Hosts the API in memory for the integration tests (ADR-0007). Each test class gets its own
// instance through IClassFixture<CatalogApiFactory>. Later stages attach the per-class test
// database and any configuration overrides here.
public sealed class CatalogApiFactory : WebApplicationFactory<Program>
{
    // Not Development: user secrets and Development-only features stay off unless a test opts in.
    public const string EnvironmentName = "Testing";

    // The Testing environment has no connection string of its own (ADR-0009), and the host does not
    // start without one. No test connects to this one; Stage 4.2 replaces it with a database per
    // test class.
    public const string ConnectionString = "Data Source=no-database.invalid;Initial Catalog=eShopCatalog";

    // WebApplicationFactory passes host settings to Program as command-line arguments, which
    // CreateBuilder reads first, so Program.cs sees them while it registers services; they also
    // override environment variables. ConfigureAppConfiguration would apply only after that.
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(EnvironmentName)
            .UseSetting("ConnectionStrings:CatalogDb", ConnectionString);
}
