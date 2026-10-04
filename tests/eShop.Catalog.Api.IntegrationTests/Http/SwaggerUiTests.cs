using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace eShop.Catalog.Api.IntegrationTests.Http;

// Swagger UI at /swagger, over the OpenAPI document that the API serves at /openapi/v1.json, in Development only
// (ADR-0028).
public sealed class SwaggerUiTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // /swagger redirects to /swagger/index.html, and the client follows. The UI reads its settings, with the URL of the
    // document that it shows, from /swagger/index.js.
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
