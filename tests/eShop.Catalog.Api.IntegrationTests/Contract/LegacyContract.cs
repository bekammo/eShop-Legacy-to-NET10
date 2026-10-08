using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.IntegrationTests.Contract;

// The golden exchanges of the ported endpoints, and how the new API must answer them: the comparison rules of
// docs/legacy/README.md (ADR-0002). LegacyContractTests replays them.
internal static class LegacyContract
{
    // The contract files of the ported and retired endpoints.
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

    // The exchanges that the new API answers differently on purpose, by the delta that records it in
    // docs/behavior-changes.md (rule 3). The request is replayed as recorded, and the response is compared with the
    // recorded response of the exchange named in AnswerOf; or, for a delta that answers with an error of its own, with
    // the Status alone (rules 7 and 12); or, for a Partial delta, with the part of the recorded body that the request's
    // Range header names.
    public static readonly IReadOnlyDictionary<string, Delta> Deltas = new Dictionary<string, Delta>
    {
        // No XML: the JSON of the matching JSON exchange (rule 8).
        ["brands-get-all--accept-xml"] = new("BC-002", AnswerOf: "brands-get-all--accept-json"),
        ["brands-get-all--accept-text-xml"] = new("BC-002", AnswerOf: "brands-get-all--accept-json"),
        ["brands-get-all--accept-browser"] = new("BC-002", AnswerOf: "brands-get-all--accept-json"),
        ["brands-get-by-id--xml"] = new("BC-002", AnswerOf: "brands-get-by-id--json"),
        ["brands-get-by-id--non-integer-xml"] = new("BC-002", AnswerOf: "brands-get-by-id--non-integer"),

        // Kestrel answers OPTIONS as any other method that the route does not have: 405 with Allow: GET, as POST.
        ["brands-options"] = new("BC-003", AnswerOf: "brands-post"),

        // The dotted ID reaches the API, which cannot bind it, as abc.
        ["brands-get-by-id--dot-in-segment"] = new("BC-004", AnswerOf: "brands-get-by-id--non-integer"),

        // The query string does not choose the endpoint: the list.
        ["brands-get-by-id--query-string"] = new("BC-005", AnswerOf: "brands-get-all--accept-json"),

        // Retired: 410 Gone, whatever the Accept header and the ID.
        ["files-get"] = new("BC-006", Status: 410),
        ["files-get--accept-json"] = new("BC-006", Status: 410),
        ["files-get-by-id"] = new("BC-006", Status: 410),

        // The bytes that the Range header asks for, with 206, where the legacy app sent the whole picture.
        ["pic-get--range"] = new("BC-010", Partial: true),

        // Routing answers a method that the route does not have with 405 and Allow: GET, as on the brand routes, where
        // MVC answered 404.
        ["pic-head"] = new("BC-011", AnswerOf: "brands-head"),
        ["pic-post"] = new("BC-011", AnswerOf: "brands-post"),
    };

    // The delta of an exchange that has none.
    public const string NoDelta = "none";

    // Each exchange, with the ID of its delta, so that the name of each case shows the delta.
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

    // Replays the exchange, and checks the response against the recorded one, or against what its delta names.
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

        // Rule 7: an error's body is not contract. Only its status counts, and the Allow header of a 405, as a set.
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
            // Rule 6.
            case "empty":
                Assert.True(body.Length == 0, $"{because}: a body of {body.Length} bytes where none was expected.");
                break;

            // Rule 5: equal JSON trees, numbers by value, names by case, arrays in order, and no member missing or extra.
            case "json":
                AssertMediaType(expected, response);
                var json = Encoding.UTF8.GetString(body);
                Assert.True(
                    JsonNode.DeepEquals(expected.Json, JsonNode.Parse(json)),
                    $"{because}: the body {json} differs from {expected.Json!.ToJsonString()}.");
                break;

            // Rule 9: the same length and SHA-256.
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

    // A Range request that the new API honours: 206, with the part of the recorded body that the Range header names.
    // The recording holds the whole body, as a hash, so the request is sent again without its Range header: that answer
    // must be the recorded body, and the part must be its slice.
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

    // Rule 4: the media type of a body, and charset=utf-8 where the legacy response had it.
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

    // A delta names one of: the exchange whose answer the new API gives, the status of its error, or a Partial answer
    // to a Range request.
    public sealed record Delta(string Id, string? AnswerOf = null, int? Status = null, bool Partial = false);
}
