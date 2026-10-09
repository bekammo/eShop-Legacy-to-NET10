using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;

namespace eShop.Catalog.Api.IntegrationTests.Http;

public sealed class OpenApiDocumentTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
{
    private const string SnapshotInRepository = "docs/openapi/v1.json";

    private static readonly string Snapshot = Path.Combine(AppContext.BaseDirectory, "OpenApi", "v1.json");

    private static readonly string Received = Path.ChangeExtension(Snapshot, ".received.json");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Document_matches_the_committed_snapshot()
    {
        File.Delete(Received);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var served = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!.AsObject();
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

    [Fact]
    public async Task Only_the_item_writes_ask_for_a_bearer_token_with_the_write_scope()
    {
        var document = await ServedDocumentAsync();

        var scheme = document["components"]!["securitySchemes"]!["Bearer"]!;
        Assert.Equal("http", (string?)scheme["type"]);
        Assert.Equal("bearer", (string?)scheme["scheme"]);
        Assert.Equal("JWT", (string?)scheme["bearerFormat"]);
        var secured = Operations(document).Where(static operation => operation.Operation["security"] is not null).ToList();
        Assert.Equal(["POST /api/items", "PUT /api/items/{id}", "DELETE /api/items/{id}"], secured.Select(static operation => operation.Name));
        Assert.All(secured, static operation =>
            Assert.Equal("""[{"Bearer":["catalog:write"]}]""", operation.Operation["security"]!.ToJsonString()));
    }

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

    private static IEnumerable<(string Name, JsonObject Operation)> Operations(JsonObject document) =>
        document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject().Select(operation =>
            ($"{operation.Key.ToUpperInvariant()} {path.Key}", operation.Value!.AsObject())));
}
