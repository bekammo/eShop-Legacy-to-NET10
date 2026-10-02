using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.IntegrationTests.Contract;

// The golden exchanges of the endpoints ported so far, and how the new API must answer them: the comparison rules of
// docs/legacy/README.md (ADR-0002). LegacyContractTests replays them.
internal static class LegacyContract
{
    // The contract files of the ported endpoints. A file joins when its endpoint is ported.
    public static readonly IReadOnlyList<string> Files =
    [
        "api-root.json",
        "brands-list.json",
        "brands-get-by-id.json",
        "brands-delete.json",
        "brands-other-verbs.json",
    ];

    public static readonly IReadOnlyDictionary<string, GoldenExchange> Exchanges = GoldenExchange.Load(Files);

    // The exchanges that the new API answers differently on purpose, by the delta that records it in
    // docs/behavior-changes.md (rule 3). The request is replayed as recorded, and the response is compared with the
    // recorded response of the exchange named in AnswerOf.
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

    // Replays the exchange, and checks the response against the recorded one, or against the one that its delta names.
    public static async Task ReplayAsync(HttpClient client, string name, string deltaId, CancellationToken cancellationToken)
    {
        var delta = Deltas.GetValueOrDefault(name);
        Assert.Equal(deltaId, delta?.Id ?? NoDelta);
        var expected = delta is null ? Exchanges[name] : Exchanges[delta.AnswerOf];
        using var request = Exchanges[name].CreateRequest();

        using var response = await client.SendAsync(request, cancellationToken);

        var because = delta is null ? name : $"{name}, answered as {delta.AnswerOf} ({delta.Id})";
        Assert.True(expected.Status == (int)response.StatusCode, $"{because}: status {(int)response.StatusCode}, expected {expected.Status}.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

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
                Assert.True(body.Length == 0, $"{because}: a body where none was expected: {body}");
                break;

            // Rule 4: the media type, and charset=utf-8 where the legacy response had it. Rule 5: equal JSON trees,
            // numbers by value, names by case, arrays in order, and no member missing or extra.
            case "json":
                var legacyType = MediaTypeHeaderValue.Parse(expected.ResponseHeader("Content-Type")!);
                var type = response.Content.Headers.ContentType;
                Assert.Equal(legacyType.MediaType, type?.MediaType, ignoreCase: true);
                if (legacyType.CharSet is { } charset)
                {
                    Assert.Equal(charset, type?.CharSet, ignoreCase: true);
                }

                Assert.True(
                    JsonNode.DeepEquals(expected.Json, JsonNode.Parse(body)),
                    $"{because}: the body {body} differs from {expected.Json!.ToJsonString()}.");
                break;

            default:
                Assert.Fail($"{because}: no comparison for a {expected.BodyKind} body.");
                break;
        }
    }

    private static SortedSet<string> Methods(IEnumerable<string> methods) =>
        new(methods.Select(static method => method.Trim().ToUpperInvariant()), StringComparer.Ordinal);

    public sealed record Delta(string Id, string AnswerOf);
}
