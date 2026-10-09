using System.Net;
using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.IntegrationTests.Types;

[Trait("Category", "Docker")]
public sealed class TypeEndpointsTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    [Fact]
    public async Task Types_are_listed_in_ID_order()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/types", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""[{"Id":1,"Type":"Mug"},{"Id":2,"Type":"T-Shirt"},{"Id":3,"Type":"Sheet"},{"Id":4,"Type":"USB Memory Stick"}]"""),
            JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))));
    }
}
