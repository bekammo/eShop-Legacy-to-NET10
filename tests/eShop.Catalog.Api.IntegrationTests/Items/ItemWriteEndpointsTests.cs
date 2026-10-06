using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.IntegrationTests.Items;

// Creating, updating and deleting items (ADR-0025, ADR-0026), on a database of the class's own, which the writes change.
[Trait("Category", "Docker")]
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

    // A full replacement: every field is the request's, OnReorder included, and a posted ID and picture name change
    // nothing: the item keeps its ID and its picture (ADR-0026).
    [Fact]
    public async Task Update_writes_every_field_and_keeps_the_ID_and_the_picture()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client);
        var changed = ValidBody();
        changed["Name"] = "Changed mug";
        changed["Description"] = null;
        changed["Price"] = 12.5m;
        changed["CatalogTypeId"] = 3;
        changed["CatalogBrandId"] = 5;
        changed["AvailableStock"] = 20;
        changed["RestockThreshold"] = 5;
        changed["MaxStockThreshold"] = 60;
        changed["OnReorder"] = true;
        changed["Id"] = 1;
        changed["PictureFileName"] = "../Global.asax";

        using var response = await client.PutAsJsonAsync($"/api/items/{id}", changed, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var item = JsonNode.Parse(await client.GetStringAsync($"/api/items/{id}", CancellationToken))!;
        Assert.Equal("Changed mug", (string)item["Name"]!);
        Assert.Null(item["Description"]);
        Assert.Equal(12.5m, (decimal)item["Price"]!);
        Assert.Equal(id, (int)item["Id"]!);
        Assert.Equal("Sheet", (string)item["CatalogType"]!["Type"]!);
        Assert.Equal("Other", (string)item["CatalogBrand"]!["Brand"]!);
        Assert.Equal(20, (int)item["AvailableStock"]!);
        Assert.Equal(5, (int)item["RestockThreshold"]!);
        Assert.Equal(60, (int)item["MaxStockThreshold"]!);
        Assert.True((bool)item["OnReorder"]!);
        using var picture = await client.GetAsync($"/items/{id}/pic", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
    }

    // Audit D2: the legacy edit wrote defaults over the fields that a post left out. A missing field is a 400, and the
    // item does not change.
    [Fact]
    public async Task Update_without_a_required_field_is_a_400_and_changes_nothing()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client);
        var before = await client.GetStringAsync($"/api/items/{id}", CancellationToken);

        using var response = await client.PutAsJsonAsync($"/api/items/{id}", ValidBody("AvailableStock", null), CancellationToken);

        await AssertValidationProblemAsync(response, "AvailableStock");
        Assert.Equal(before, await client.GetStringAsync($"/api/items/{id}", CancellationToken));
    }

    [Fact]
    public async Task Update_with_an_unknown_brand_is_a_400_problem_that_names_it()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client);

        using var response = await client.PutAsJsonAsync($"/api/items/{id}", ValidBody("CatalogBrandId", "999"), CancellationToken);

        await AssertValidationProblemAsync(response, "CatalogBrandId");
    }

    // The legacy delete removed the row, and its picture route answered 404 afterwards (delete-item).
    [Fact]
    public async Task Delete_removes_the_item_and_its_picture()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client);

        using var response = await client.DeleteAsync($"/api/items/{id}", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var item = await client.GetAsync($"/api/items/{id}", CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, item.StatusCode);
        using var picture = await client.GetAsync($"/items/{id}/pic", CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
    }

    // Audit D11 (unknown-item-writes): the legacy edit and delete of item 999 answered 500.
    [Theory]
    [InlineData("PUT", "999", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "999", HttpStatusCode.NotFound)]
    [InlineData("PUT", "abc", HttpStatusCode.BadRequest)]
    [InlineData("DELETE", "abc", HttpStatusCode.BadRequest)]
    public async Task Write_to_an_unknown_item_is_a_404_and_to_an_ID_that_is_not_an_integer_a_400(string method, string id, HttpStatusCode status)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/items/{id}")
        {
            Content = method == "PUT" ? JsonContent.Create(ValidBody()) : null,
        };

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(status, response.StatusCode);
    }

    private static async Task<int> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/items", ValidBody(), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (int)JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!["Id"]!;
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
