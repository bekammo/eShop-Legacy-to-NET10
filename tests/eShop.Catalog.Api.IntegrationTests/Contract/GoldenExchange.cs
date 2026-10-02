using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.IntegrationTests.Contract;

// One exchange of docs/legacy/contract: a request to the legacy app, and the response that it got
// (docs/legacy/README.md, "Golden exchange format").
internal sealed class GoldenExchange(string name, JsonObject exchange)
{
    public string Name { get; } = name;

    public int Status => Response["status"]!.GetValue<int>();

    // empty, json, xml, html or binary.
    public string BodyKind => Response["body"]!["kind"]!.GetValue<string>();

    public JsonNode? Json => Response["body"]!["json"];

    // A binary body is recorded as its length and its SHA-256 (rule 9).
    public long BodyLength => Response["body"]!["length"]!.GetValue<long>();

    public string BodySha256 => Response["body"]!["sha256"]!.GetValue<string>();

    private JsonObject Request => exchange["request"]!.AsObject();

    private JsonObject Response => exchange["response"]!.AsObject();

    // The exchanges of the files, by name, which is unique across the files.
    public static IReadOnlyDictionary<string, GoldenExchange> Load(IEnumerable<string> files) =>
        files
            .SelectMany(static file => LegacyFiles.ReadJson(Path.Combine("contract", file))["exchanges"]!.AsObject())
            .ToDictionary(static exchange => exchange.Key, static exchange => new GoldenExchange(exchange.Key, exchange.Value!.AsObject()));

    public string? RequestHeader(string header) => Request["headers"]![header]?.GetValue<string>();

    public string? ResponseHeader(string header) => Response["headers"]![header]?.GetValue<string>();

    // The request as it was recorded: the method, the path as it is, exactly the recorded headers, and the body (rule 2).
    // Without the header named, if one is.
    public HttpRequestMessage CreateRequest(string? without = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(Request["method"]!.GetValue<string>()), Request["path"]!.GetValue<string>());
        if (Request["body"] is { } body)
        {
            Assert.Equal("json", body["kind"]!.GetValue<string>());
            request.Content = new StringContent(body["json"]!.ToJsonString(), Encoding.UTF8);
            request.Content.Headers.ContentType = null;
        }

        foreach (var (header, value) in Request["headers"]!.AsObject().Where(header => header.Key != without))
        {
            if (header == "Content-Type")
            {
                Assert.NotNull(request.Content);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value!.GetValue<string>());
            }
            else
            {
                Assert.True(request.Headers.TryAddWithoutValidation(header, value!.GetValue<string>()), header);
            }
        }

        return request;
    }
}
