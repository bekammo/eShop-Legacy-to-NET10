using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace eShop.Catalog.Api.IntegrationTests.Http;

public sealed class SwaggerUiTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Swagger_UI_shows_the_served_document_in_Development()
    {
        await using var host = factory.WithWebHostBuilder(static builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient();

        using var page = await client.GetAsync("/swagger", CancellationToken);
        var settings = await client.GetStringAsync("/swagger/index.js", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("""{"url":"/openapi/v1.json","name":"v1"}""", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Swagger_UI_is_not_served_in_Production()
    {
        await using var host = factory.WithWebHostBuilder(static builder => builder.UseEnvironment("Production"));
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/swagger/index.html", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
