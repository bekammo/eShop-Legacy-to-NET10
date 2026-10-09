using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace eShop.Catalog.Api.IntegrationTests.Authorization;

// The item writes need an access token with the catalog:write scope (ADR-0034), in mock mode, where nothing else stands
// between a request and the catalog. The reads stay anonymous: the tests of each read endpoint, and the golden exchanges
// of /api/brands, send no token.
public sealed class WriteAuthorizationTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Writes => new()
    {
        { "POST", "/api/items" },
        { "PUT", "/api/items/1" },
        { "DELETE", "/api/items/1" },
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Write_without_a_token_is_a_401_problem_that_asks_for_a_bearer_token(string method, string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Request(method, path, token: null), CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData("signed with another key")]
    [InlineData("issued by another issuer")]
    [InlineData("issued for another audience")]
    [InlineData("expired")]
    public async Task Token_that_is_not_valid_is_a_401(string token)
    {
        using var client = factory.CreateClient();
        var value = token switch
        {
            "signed with another key" => AccessTokens.Create("catalog:write", key: new SymmetricSecurityKey(new byte[32])),
            "issued by another issuer" => AccessTokens.Create("catalog:write", issuer: "another-issuer"),
            "issued for another audience" => AccessTokens.Create("catalog:write", audience: "another-api"),
            _ => AccessTokens.Create("catalog:write", expired: DateTime.UtcNow.AddHours(-1)),
        };

        using var response = await client.SendAsync(Request("POST", "/api/items", AccessTokens.Bearer(value)), CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Token_without_the_write_scope_is_a_403_problem(string method, string path)
    {
        using var client = factory.CreateClient();
        var token = AccessTokens.Bearer(AccessTokens.Create("catalog:read"));

        using var response = await client.SendAsync(Request(method, path, token), CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    // The scope claim as dotnet user-jwts writes it, a string for one scope and an array for more, and as an authorization
    // server writes it, one string that separates the scopes with spaces (RFC 9068).
    public static TheoryData<object> WriteScopes => ["catalog:write", new[] { "catalog:read", "catalog:write" }, "catalog:read catalog:write"];

    [Theory]
    [MemberData(nameof(WriteScopes))]
    public async Task Token_with_the_write_scope_creates_updates_and_deletes(object scope)
    {
        using var client = factory.CreateClient();
        var token = AccessTokens.Bearer(AccessTokens.Create(scope));

        using var created = await client.SendAsync(Request("POST", "/api/items", token), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = created.Headers.Location!.OriginalString;
        using var updated = await client.SendAsync(Request("PUT", path, token), CancellationToken);
        using var deleted = await client.SendAsync(Request("DELETE", path, token), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    // A valid item, so that the request would succeed but for its token.
    private static HttpRequestMessage Request(string method, string path, AuthenticationHeaderValue? token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = token;
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new JsonObject
            {
                ["Name"] = "Test mug",
                ["Price"] = 9.99m,
                ["CatalogTypeId"] = 1,
                ["CatalogBrandId"] = 2,
                ["AvailableStock"] = 10,
                ["RestockThreshold"] = 2,
                ["MaxStockThreshold"] = 50,
                ["OnReorder"] = false,
            });
        }

        return request;
    }

    // Authentication's challenge and forbid set only the status. The status code pages give it the problem body
    // (ADR-0021).
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Equal((int)status, (int)problem["status"]!);
        Assert.False(string.IsNullOrEmpty((string?)problem["traceId"]));
    }
}
