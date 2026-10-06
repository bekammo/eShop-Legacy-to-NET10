using Microsoft.AspNetCore.Hosting;

namespace eShop.Catalog.Api.IntegrationTests;

// The API in mock mode (ADR-0017): it serves the catalog from memory and registers nothing of the database, so its
// test classes need no Docker (ADR-0031). It hosts the classes whose subject is not the catalog's data, such as the
// error contract, the logs and the OpenAPI document, and mock mode itself in the classes that compare it with the
// database. It never asks SqlServerFixture for a database.
public sealed class MockModeCatalogApiFactory(SqlServerFixture sqlServer) : CatalogApiFactory(sqlServer)
{
    // Builds the factory's own host before any test derives one, as CatalogApiFactory does, so that its host is the one
    // that writes to LogFilePath.
    public override ValueTask InitializeAsync()
    {
        _ = Services;
        return ValueTask.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Catalog:UseMockData", "true");
    }
}
