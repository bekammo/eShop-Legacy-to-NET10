using Microsoft.AspNetCore.Hosting;

namespace eShop.Catalog.Api.IntegrationTests;

public sealed class MockModeCatalogApiFactory(SqlServerFixture sqlServer) : CatalogApiFactory(sqlServer)
{
    // Builds this factory's host now, without base's database, so that it is host 1 and writes to LogFilePath; a host
    // that a test derives first would otherwise take that file.
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
