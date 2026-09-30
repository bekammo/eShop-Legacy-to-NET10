using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.UnitTests.Catalog;

// The legacy sample data, which the migrations, the seeder and the in-memory catalog share (ADR-0016). The brands
// and types are also checked as the model's seed data, in CatalogModelTests.
public sealed class PreconfiguredDataTests
{
    [Fact]
    public void Brands_are_the_legacy_ones() =>
        Assert.Equal(LegacySeedData.Brands, PreconfiguredData.CatalogBrands().Select(brand => LegacySeedData.Row(brand.Id, brand.Brand)));

    [Fact]
    public void Types_are_the_legacy_ones() =>
        Assert.Equal(LegacySeedData.Types, PreconfiguredData.CatalogTypes().Select(type => LegacySeedData.Row(type.Id, type.Type)));

    // HiLo numbers the items in the order they are added, from 1 in a new database (ADR-0013), and the in-memory
    // catalog numbers them the same way.
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

    // The callers track or change what they get.
    [Fact]
    public void Each_call_returns_new_instances()
    {
        Assert.NotSame(PreconfiguredData.CatalogBrands()[0], PreconfiguredData.CatalogBrands()[0]);
        Assert.NotSame(PreconfiguredData.CatalogTypes()[0], PreconfiguredData.CatalogTypes()[0]);
        Assert.NotSame(PreconfiguredData.CatalogItems()[0], PreconfiguredData.CatalogItems()[0]);
    }
}
