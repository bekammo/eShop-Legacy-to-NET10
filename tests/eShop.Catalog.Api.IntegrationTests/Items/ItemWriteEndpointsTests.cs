using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.IntegrationTests.Items;

// Creating items (ADR-0025), on a database of the class's own, which the writes change.
public sealed class ItemWriteEndpointsTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Created_item_gets_a_new_ID_the_default_picture_and_its_location()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/items", ValidBody(), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        var id = (int)created["Id"]!;
        Assert.Equal($"/api/items/{id}", response.Headers.Location?.OriginalString);
        Assert.True(JsonNode.DeepEquals(created, JsonNode.Parse(await client.GetStringAsync($"/api/items/{id}", CancellationToken))));
        Assert.Equal("Mug", (string)created["CatalogType"]!["Type"]!);
        using var picture = await client.GetAsync(new Uri((string)created["PictureUri"]!).PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
    }

    // The legacy create replaced a posted ID, and took a posted picture name (audit D1, D2). The request has neither.
    [Fact]
    public async Task Posted_ID_and_picture_name_are_ignored()
    {
        using var client = factory.CreateClient();
        var body = ValidBody();
        body["Id"] = 1;
        body["PictureFileName"] = "../Global.asax";

        using var response = await client.PostAsJsonAsync("/api/items", body, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.NotEqual(1, (int)created["Id"]!);
        using var picture = await client.GetAsync(new Uri((string)created["PictureUri"]!).PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal(".NET Bot Black Hoodie", (string)JsonNode.Parse(await client.GetStringAsync("/api/items/1", CancellationToken))!["Name"]!);
    }

    // The rules (create-item-validation), each broken alone. null removes the field, which must then be reported as
    // required, and not only by the check of the brand and the type.
    [Theory]
    [InlineData("Name", null)]
    [InlineData("Name", "\"\"")]
    [InlineData("Name", "\"123456789012345678901234567890123456789012345678901\"")]
    [InlineData("Price", null)]
    [InlineData("Price", "-1")]
    [InlineData("Price", "-0.01")]
    [InlineData("Price", "1.005")]
    [InlineData("Price", "1000000.01")]
    [InlineData("Price", "1000000.50")]
    [InlineData("CatalogTypeId", null)]
    [InlineData("CatalogBrandId", null)]
    [InlineData("AvailableStock", null)]
    [InlineData("AvailableStock", "-1")]
    [InlineData("AvailableStock", "10000001")]
    [InlineData("RestockThreshold", null)]
    [InlineData("RestockThreshold", "-1")]
    [InlineData("RestockThreshold", "10000001")]
    [InlineData("MaxStockThreshold", null)]
    [InlineData("MaxStockThreshold", "-1")]
    [InlineData("MaxStockThreshold", "10000001")]
    [InlineData("OnReorder", null)]
    public async Task Field_that_breaks_its_rule_is_a_400_problem_that_names_it(string field, string? value)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/items", ValidBody(field, value), CancellationToken);

        var errors = await AssertValidationProblemAsync(response, field);
        if (value is null)
        {
            Assert.Contains("required", errors[field]!.ToJsonString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Name", "\"12345678901234567890123456789012345678901234567890\"")]
    [InlineData("Price", "0")]
    [InlineData("Price", "1000000")]
    [InlineData("Price", "1000000.00")]
    [InlineData("Price", "8.5")]
    [InlineData("Description", null)]
    [InlineData("MaxStockThreshold", "10000000")]
    public async Task Value_at_the_edge_of_its_rule_is_accepted(string field, string? value)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/items", ValidBody(field, value), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Audit D10: the legacy app answered with a 500, from the foreign key.
    [Theory]
    [InlineData("CatalogBrandId")]
    [InlineData("CatalogTypeId")]
    public async Task Unknown_brand_or_type_is_a_400_problem_that_names_it(string field)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/items", ValidBody(field, "999"), CancellationToken);

        await AssertValidationProblemAsync(response, field);
    }

    [Fact]
    public async Task Body_that_is_not_JSON_is_a_400_problem()
    {
        using var client = factory.CreateClient();
        using var body = new StringContent("{", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/items", body, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // The legacy app's limit, httpRuntime's default. The test server does not apply Kestrel's limits, so the setting is
    // checked.
    [Fact]
    public void Request_body_is_limited_to_the_legacy_4_MB()
    {
        Assert.Equal(4 * 1024 * 1024, factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize);
    }

    // A valid item, with one field changed, or removed when the value is null.
    private static JsonObject ValidBody(string? field = null, string? value = null)
    {
        var body = new JsonObject
        {
            ["Name"] = "Test mug",
            ["Description"] = "A mug for the tests",
            ["Price"] = 9.99m,
            ["CatalogTypeId"] = 1,
            ["CatalogBrandId"] = 2,
            ["AvailableStock"] = 10,
            ["RestockThreshold"] = 2,
            ["MaxStockThreshold"] = 50,
            ["OnReorder"] = false,
        };
        if (field is not null)
        {
            if (value is null)
            {
                body.Remove(field);
            }
            else
            {
                body[field] = JsonNode.Parse(value);
            }
        }

        return body;
    }

    // The problem's errors, which name the field.
    private static async Task<JsonNode> AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.True(problem["errors"]?[field] is not null, problem.ToJsonString());
        return problem["errors"]!;
    }
}
