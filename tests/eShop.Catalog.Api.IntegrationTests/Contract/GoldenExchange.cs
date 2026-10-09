using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.IntegrationTests.Contract;

internal sealed class GoldenExchange(string name, JsonObject exchange)
{
    public string Name { get; } = name;

    public int Status => Response["status"]!.GetValue<int>();

    public string BodyKind => Response["body"]!["kind"]!.GetValue<string>();

    public JsonNode? Json => Response["body"]!["json"];

    public long BodyLength => Response["body"]!["length"]!.GetValue<long>();

    public string BodySha256 => Response["body"]!["sha256"]!.GetValue<string>();

    private JsonObject Request => exchange["request"]!.AsObject();

    private JsonObject Response => exchange["response"]!.AsObject();

    public static IReadOnlyDictionary<string, GoldenExchange> Load(IEnumerable<string> files) =>
        files
            .SelectMany(static file => LegacyFiles.ReadJson(Path.Combine("contract", file))["exchanges"]!.AsObject())
            .ToDictionary(static exchange => exchange.Key, static exchange => new GoldenExchange(exchange.Key, exchange.Value!.AsObject()));

    public string? RequestHeader(string header) => Request["headers"]![header]?.GetValue<string>();

    public string? ResponseHeader(string header) => Response["headers"]![header]?.GetValue<string>();

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
