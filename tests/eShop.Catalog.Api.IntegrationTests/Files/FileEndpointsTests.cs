using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.IntegrationTests.Files;

// GET /api/files, retired (ADR-0022): 410 Gone with a problem that points to GET /api/brands. LegacyContractTests
// replays its golden exchanges against BC-006.
public sealed class FileEndpointsTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/files", null)]
    [InlineData("/api/files", "application/json")]
    [InlineData("/api/files", "application/octet-stream")]
    [InlineData("/api/files/1", null)]
    [InlineData("/API/FILES/abc", null)]
    public async Task Get_is_410_Gone_with_a_problem_that_points_to_the_brands(string path, string? accept)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.ToString());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.11", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("Gone", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(410, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("GET /api/brands", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retired_endpoint_is_not_in_the_OpenAPI_document()
    {
        using var client = factory.CreateClient();

        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", CancellationToken))!;

        Assert.DoesNotContain(document["paths"]!.AsObject(), static path => path.Key.StartsWith("/api/files", StringComparison.OrdinalIgnoreCase));
    }
}
