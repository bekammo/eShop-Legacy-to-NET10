using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace eShop.Catalog.Api.IntegrationTests.Items;

// The item reads (ADR-0024), on the class's database, which holds the 12 sample items, and in mock mode.
[Trait("Category", "Docker")]
public sealed class ItemEndpointsTests(CatalogApiFactory factory, MockModeCatalogApiFactory mockMode)
    : IClassFixture<CatalogApiFactory>, IClassFixture<MockModeCatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The legacy Index action's defaults: page 0 of 10 items.
    [Fact]
    public async Task First_page_of_ten_items_in_ID_order_is_the_default()
    {
        using var client = factory.CreateClient();

        var page = await GetJsonAsync(client, "/api/items");

        Assert.Equal(0, (int)page["ActualPage"]!);
        Assert.Equal(10, (int)page["ItemsPerPage"]!);
        Assert.Equal(12, (long)page["TotalItems"]!);
        Assert.Equal(2, (long)page["TotalPages"]!);
        Assert.Equal(Enumerable.Range(1, 10), page["Data"]!.AsArray().Select(static item => (int)item!["Id"]!));
    }

    [Fact]
    public async Task Page_holds_the_items_of_its_index_and_a_page_after_the_last_is_empty()
    {
        using var client = factory.CreateClient();

        var last = await GetJsonAsync(client, "/api/items?pageSize=5&pageIndex=2");
        var past = await GetJsonAsync(client, "/api/items?pageSize=5&pageIndex=100");

        Assert.Equal([11, 12], last["Data"]!.AsArray().Select(static item => (int)item!["Id"]!));
        Assert.Equal(3, (long)last["TotalPages"]!);
        Assert.Empty(past["Data"]!.AsArray());
        Assert.Equal(12, (long)past["TotalItems"]!);
    }

    // Audit D6: the legacy app answered 0 and negative values with a 500, and read any page size.
    [Theory]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=-1", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageIndex=-1", "pageIndex")]
    [InlineData("pageSize=abc", null)]
    [InlineData("pageIndex=abc", null)]
    public async Task Paging_outside_its_bounds_is_a_400_problem(string query, string? invalid)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/items?{query}", CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        if (invalid is not null)
        {
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
            Assert.NotNull(problem["errors"]![invalid]);
        }
    }

    [Fact]
    public async Task Page_of_100_items_is_allowed()
    {
        using var client = factory.CreateClient();

        var page = await GetJsonAsync(client, "/api/items?pageSize=100");

        Assert.Equal(12, page["Data"]!.AsArray().Count);
    }

    // The legacy model's properties, without the picture's file name, and the absolute URL of the picture.
    [Fact]
    public async Task Item_has_the_legacy_model_s_properties_and_the_URL_of_its_picture()
    {
        using var client = factory.CreateClient();

        var item = await GetJsonAsync(client, "/api/items/1");

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {
                  "Id": 1, "Name": ".NET Bot Black Hoodie", "Description": ".NET Bot Black Hoodie", "Price": 19.50,
                  "PictureUri": "http://localhost/items/1/pic",
                  "CatalogTypeId": 2, "CatalogType": { "Id": 2, "Type": "T-Shirt" },
                  "CatalogBrandId": 2, "CatalogBrand": { "Id": 2, "Brand": ".NET" },
                  "AvailableStock": 100, "RestockThreshold": 0, "MaxStockThreshold": 0, "OnReorder": false
                }
                """),
            item),
            item.ToJsonString());
    }

    [Fact]
    public async Task Picture_URL_leads_to_the_picture()
    {
        using var client = factory.CreateClient();
        var item = await GetJsonAsync(client, "/api/items/3");

        using var response = await client.GetAsync(new Uri((string)item["PictureUri"]!).PathAndQuery, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("999", HttpStatusCode.NotFound)]
    [InlineData("0", HttpStatusCode.NotFound)]
    [InlineData("abc", HttpStatusCode.BadRequest)]
    public async Task Item_that_does_not_exist_is_a_404_and_an_ID_that_is_not_an_integer_a_400(string id, HttpStatusCode status)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/items/{id}", CancellationToken);

        Assert.Equal(status, response.StatusCode);
    }

    // Both services hold the same sample items (ADR-0016), so both modes answer the same.
    [Theory]
    [InlineData("/api/items?pageSize=12")]
    [InlineData("/api/items/9")]
    [InlineData("/api/types")]
    public async Task Mock_mode_answers_as_the_database_does(string path)
    {
        using var databaseClient = factory.CreateClient();
        using var memoryClient = mockMode.CreateClient(new WebApplicationFactoryClientOptions());

        var database = await GetJsonAsync(databaseClient, path);
        var memory = await GetJsonAsync(memoryClient, path);

        Assert.True(JsonNode.DeepEquals(database, memory), $"{database.ToJsonString()}{Environment.NewLine}{memory.ToJsonString()}");
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
    }
}
