using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

namespace eShop.Catalog.Api.IntegrationTests.Http;

// The OpenAPI document that the API serves at /openapi/v1.json (ADR-0020), against the committed snapshot
// docs/openapi/v1.json, so that every change to the contract of an endpoint shows in that file's diff.
public sealed class OpenApiDocumentTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private const string SnapshotInRepository = "docs/openapi/v1.json";

    // The copy that the test project puts in its output.
    private static readonly string Snapshot = Path.Combine(AppContext.BaseDirectory, "OpenApi", "v1.json");

    // Where the test writes the served document when it differs.
    private static readonly string Received = Path.ChangeExtension(Snapshot, ".received.json");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // On a difference, the served document is written beside the snapshot's copy, in the form of the snapshot, so
    // that an intended change is accepted by copying that file over the snapshot. The file of an earlier run goes
    // first, so that a stale one cannot be copied by mistake.
    [Fact]
    public async Task Document_matches_the_committed_snapshot()
    {
        File.Delete(Received);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var served = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!.AsObject();
        // The URL of the host that served the document, which differs from host to host.
        served.Remove("servers");
        if (!JsonNode.DeepEquals(served, JsonNode.Parse(await File.ReadAllTextAsync(Snapshot, CancellationToken))))
        {
            var indented = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            await File.WriteAllTextAsync(Received, served.ToJsonString(indented) + "\n", CancellationToken);
            Assert.Fail(
                $"The OpenAPI document differs from {SnapshotInRepository}. The document that the API serves is in {Received}. " +
                $"If the change is intended, copy that file over {SnapshotInRepository}.");
        }
    }

    // The document describes the contract, which is public, so it is not a Development-only feature. Swagger UI is
    // (ADR-0028).
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Document_is_served_in_every_environment(string environment)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
