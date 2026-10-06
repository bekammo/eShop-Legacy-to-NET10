using System.Net;
using eShop.Catalog.Api.IntegrationTests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace eShop.Catalog.Api.IntegrationTests.Health;

// /health/ready (ADR-0013): ready only when the catalog database can be reached and has every migration.
[Trait("Category", "Docker")]
public sealed class ReadinessEndpointTests(CatalogApiFactory factory, SqlServerFixture sqlServer) : IClassFixture<CatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_returns_200_Healthy_when_the_database_is_migrated()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task Get_returns_503_Unhealthy_when_the_database_cannot_be_reached()
    {
        var missing = await sqlServer.NewDatabaseAsync("missing");
        using var missingDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:CatalogDb", missing));

        await AssertUnhealthyAsync(missingDatabase);
    }

    // A deployment that started the new build before applying its migrations script.
    [Fact]
    public async Task Get_returns_503_Unhealthy_when_migrations_are_missing()
    {
        var empty = await LegacyDatabase.CreateEmptyAsync(sqlServer, CancellationToken);
        using var unmigrated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:CatalogDb", empty));

        await AssertUnhealthyAsync(unmigrated);
    }

    // The body is the status only: no database name, server or exception.
    private static async Task AssertUnhealthyAsync(WebApplicationFactory<Program> host)
    {
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(CancellationToken));

        using var liveness = await client.GetAsync("/health/live", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }
}
