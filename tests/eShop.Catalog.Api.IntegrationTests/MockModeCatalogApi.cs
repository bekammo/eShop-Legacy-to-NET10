using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace eShop.Catalog.Api.IntegrationTests;

// The API in mock mode (ADR-0017), for a test class that also wants it: one host, which serves the catalog from memory
// and has no database. Its factory is never initialized, so the factory's database is never created either.
public sealed class MockModeCatalogApi(SqlServerFixture sqlServer) : IAsyncDisposable
{
    private readonly CatalogApiFactory _factory = new(sqlServer);

    private WebApplicationFactory<Program>? _host;

    public HttpClient CreateClient(WebApplicationFactoryClientOptions options) =>
        (_host ??= _factory.WithWebHostBuilder(static builder => builder.UseSetting("Catalog:UseMockData", "true"))).CreateClient(options);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
