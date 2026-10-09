using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.UnitTests.Catalog;

public sealed class PreconfiguredDataTests
{
    [Fact]
    public void Brands_are_the_legacy_ones() =>
        Assert.Equal(LegacySeedData.Brands, PreconfiguredData.CatalogBrands().Select(brand => LegacySeedData.Row(brand.Id, brand.Brand)));

    [Fact]
    public void Types_are_the_legacy_ones() =>
        Assert.Equal(LegacySeedData.Types, PreconfiguredData.CatalogTypes().Select(type => LegacySeedData.Row(type.Id, type.Type)));

    [Fact]
    public void Sample_items_are_the_legacy_ones_in_id_order()
    {
        var items = PreconfiguredData.CatalogItems();
        for (var index = 0; index < items.Count; index++)
        {
            items[index].Id = index + 1;
        }

        Assert.Equal(LegacySeedData.Items, items.Select(LegacySeedData.Item));
    }

    [Fact]
    public void Each_call_returns_new_instances()
    {
        Assert.NotSame(PreconfiguredData.CatalogBrands()[0], PreconfiguredData.CatalogBrands()[0]);
        Assert.NotSame(PreconfiguredData.CatalogTypes()[0], PreconfiguredData.CatalogTypes()[0]);
        Assert.NotSame(PreconfiguredData.CatalogItems()[0], PreconfiguredData.CatalogItems()[0]);
    }
}
