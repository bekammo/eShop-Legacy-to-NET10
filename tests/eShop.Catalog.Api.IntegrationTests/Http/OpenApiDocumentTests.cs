using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;

namespace eShop.Catalog.Api.IntegrationTests.Http;

// The OpenAPI document that the API serves at /openapi/v1.json (ADR-0020), against the committed snapshot
// docs/openapi/v1.json, so that every change to the contract of an endpoint shows in that file's diff.
public sealed class OpenApiDocumentTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
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

    // Every operation says what it does, and what its parameters, its body and its responses are (ADR-0029). The
    // generator takes that from the handler's XML comments, and skips the comments of a private handler without a
    // warning. A response that nothing describes has the name of its status, such as "Not Found".
    [Fact]
    public async Task Every_operation_is_described()
    {
        var document = await ServedDocumentAsync();

        Assert.All(Operations(document), operation =>
        {
            var (name, value) = operation;
            Assert.False(string.IsNullOrEmpty((string?)value["summary"]), $"{name} has no summary.");
            Assert.All(value["parameters"]?.AsArray() ?? [], parameter =>
                Assert.False(string.IsNullOrEmpty((string?)parameter!["description"]), $"{name}: {parameter["name"]} has no description."));
            Assert.False(value["requestBody"] is { } body && string.IsNullOrEmpty((string?)body["description"]), $"{name}: the body has no description.");
            Assert.All(value["responses"]!.AsObject(), response =>
                Assert.NotEqual(ReasonPhrases.GetReasonPhrase(int.Parse(response.Key, CultureInfo.InvariantCulture)), (string?)response.Value!["description"]));
        });
    }

    // Every error is a problem (ADR-0021), and the document says so: application/problem+json, with the schema of a
    // problem, or of a validation problem, which have the traceId that ProblemJsonWriter adds (ADR-0029).
    [Fact]
    public async Task Every_error_response_is_documented_as_a_problem()
    {
        var document = await ServedDocumentAsync();
        string[] problems = ["ProblemDetails", "HttpValidationProblemDetails"];

        var errors = Operations(document).SelectMany(operation => operation.Operation["responses"]!.AsObject()
            .Where(response => int.Parse(response.Key, CultureInfo.InvariantCulture) >= 400)
            .Select(response => (Name: $"{operation.Name} {response.Key}", Content: response.Value!["content"]?.AsObject())));

        Assert.All(errors, error =>
        {
            var mediaType = Assert.Single(error.Content ?? []);
            Assert.Equal("application/problem+json", mediaType.Key);
            Assert.Contains((string?)mediaType.Value!["schema"]!["$ref"], problems.Select(problem => $"#/components/schemas/{problem}"));
        });
        Assert.All(problems, problem =>
            Assert.Equal("string", (string?)document["components"]!["schemas"]![problem]!["properties"]!["traceId"]!["type"]));
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

    private async Task<JsonObject> ServedDocumentAsync()
    {
        using var client = factory.CreateClient();
        return JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", CancellationToken))!.AsObject();
    }

    // Each operation of the document, named by its method and path, such as "GET /api/brands/{id}".
    private static IEnumerable<(string Name, JsonObject Operation)> Operations(JsonObject document) =>
        document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject().Select(operation =>
            ($"{operation.Key.ToUpperInvariant()} {path.Key}", operation.Value!.AsObject())));
}
