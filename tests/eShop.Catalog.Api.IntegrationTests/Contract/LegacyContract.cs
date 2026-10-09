using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.IntegrationTests.Contract;

internal static class LegacyContract
{
    public static readonly IReadOnlyList<string> Files =
    [
        "api-root.json",
        "brands-list.json",
        "brands-get-by-id.json",
        "brands-delete.json",
        "brands-other-verbs.json",
        "files.json",
        "pictures.json",
    ];

    public static readonly IReadOnlyDictionary<string, GoldenExchange> Exchanges = GoldenExchange.Load(Files);

    public static readonly IReadOnlyDictionary<string, Delta> Deltas = new Dictionary<string, Delta>
    {
        ["brands-get-all--accept-xml"] = new("BC-002", AnswerOf: "brands-get-all--accept-json"),
        ["brands-get-all--accept-text-xml"] = new("BC-002", AnswerOf: "brands-get-all--accept-json"),
        ["brands-get-all--accept-browser"] = new("BC-002", AnswerOf: "brands-get-all--accept-json"),
        ["brands-get-by-id--xml"] = new("BC-002", AnswerOf: "brands-get-by-id--json"),
        ["brands-get-by-id--non-integer-xml"] = new("BC-002", AnswerOf: "brands-get-by-id--non-integer"),

        ["brands-options"] = new("BC-003", AnswerOf: "brands-post"),

        ["brands-get-by-id--dot-in-segment"] = new("BC-004", AnswerOf: "brands-get-by-id--non-integer"),

        ["brands-get-by-id--query-string"] = new("BC-005", AnswerOf: "brands-get-all--accept-json"),

        ["files-get"] = new("BC-006", Status: 410),
        ["files-get--accept-json"] = new("BC-006", Status: 410),
        ["files-get-by-id"] = new("BC-006", Status: 410),

        ["pic-get--range"] = new("BC-010", Partial: true),

        ["pic-head"] = new("BC-011", AnswerOf: "brands-head"),
        ["pic-post"] = new("BC-011", AnswerOf: "brands-post"),
    };

    public const string NoDelta = "none";

    public static TheoryData<string, string> ExchangeNames
    {
        get
        {
            var exchanges = new TheoryData<string, string>();
            foreach (var name in Exchanges.Keys)
            {
                exchanges.Add(name, Deltas.TryGetValue(name, out var delta) ? delta.Id : NoDelta);
            }

            return exchanges;
        }
    }

    public static async Task ReplayAsync(HttpClient client, string name, string deltaId, CancellationToken cancellationToken)
    {
        var delta = Deltas.GetValueOrDefault(name);
        Assert.Equal(deltaId, delta?.Id ?? NoDelta);
        using var request = Exchanges[name].CreateRequest();

        using var response = await client.SendAsync(request, cancellationToken);

        if (delta is { Status: { } status })
        {
            Assert.True(status == (int)response.StatusCode, $"{name} ({delta.Id}): status {(int)response.StatusCode}, expected {status}.");
            return;
        }

        if (delta is { Partial: true })
        {
            await AssertPartAsync(client, Exchanges[name], response, cancellationToken);
            return;
        }

        var expected = delta is null ? Exchanges[name] : Exchanges[delta.AnswerOf!];
        var because = delta is null ? name : $"{name}, answered as {delta.AnswerOf} ({delta.Id})";
        Assert.True(expected.Status == (int)response.StatusCode, $"{because}: status {(int)response.StatusCode}, expected {expected.Status}.");
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (expected.Status >= 400)
        {
            if (expected.Status == 405)
            {
                Assert.Equal(Methods(expected.ResponseHeader("Allow")!.Split(',')), Methods(response.Content.Headers.Allow));
            }

            return;
        }

        switch (expected.BodyKind)
        {
            case "empty":
                Assert.True(body.Length == 0, $"{because}: a body of {body.Length} bytes where none was expected.");
                break;

            case "json":
                AssertMediaType(expected, response);
                var json = Encoding.UTF8.GetString(body);
                Assert.True(
                    JsonNode.DeepEquals(expected.Json, JsonNode.Parse(json)),
                    $"{because}: the body {json} differs from {expected.Json!.ToJsonString()}.");
                break;

            case "binary":
                AssertMediaType(expected, response);
                Assert.True(expected.BodyLength == body.LongLength, $"{because}: {body.LongLength} bytes, expected {expected.BodyLength}.");
                Assert.Equal(expected.BodySha256, Sha256(body));
                break;

            default:
                Assert.Fail($"{because}: no comparison for a {expected.BodyKind} body.");
                break;
        }
    }

    private static async Task AssertPartAsync(HttpClient client, GoldenExchange exchange, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        AssertMediaType(exchange, response);
        var range = Assert.Single(RangeHeaderValue.Parse(exchange.RequestHeader("Range")!).Ranges);
        Assert.Equal(new ContentRangeHeaderValue(range.From!.Value, range.To!.Value, exchange.BodyLength), response.Content.Headers.ContentRange);
        var part = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        using var request = exchange.CreateRequest(without: "Range");
        using var whole = await client.SendAsync(request, cancellationToken);
        var body = await whole.Content.ReadAsByteArrayAsync(cancellationToken);

        Assert.Equal(exchange.BodySha256, Sha256(body));
        Assert.Equal(body[(int)range.From.Value..((int)range.To.Value + 1)], part);
    }

    private static void AssertMediaType(GoldenExchange expected, HttpResponseMessage response)
    {
        var legacyType = MediaTypeHeaderValue.Parse(expected.ResponseHeader("Content-Type")!);
        var type = response.Content.Headers.ContentType;
        Assert.Equal(legacyType.MediaType, type?.MediaType, ignoreCase: true);
        if (legacyType.CharSet is { } charset)
        {
            Assert.Equal(charset, type?.CharSet, ignoreCase: true);
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static SortedSet<string> Methods(IEnumerable<string> methods) =>
        new(methods.Select(static method => method.Trim().ToUpperInvariant()), StringComparer.Ordinal);

    public sealed record Delta(string Id, string? AnswerOf = null, int? Status = null, bool Partial = false);
}
