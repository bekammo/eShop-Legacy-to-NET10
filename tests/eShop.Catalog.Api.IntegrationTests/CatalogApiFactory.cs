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

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(EnvironmentName);
}
