using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.UnitTests.Data;

public sealed class SampleItemSeederTests
{
    // HiLo numbers the items in the order they are added, from 1 in a new database (ADR-0013).
    [Fact]
    public void Sample_items_are_the_legacy_ones_in_id_order()
    {
        var items = SampleItemSeeder.CreateItems();
        for (var index = 0; index < items.Count; index++)
        {
            items[index].Id = index + 1;
        }

        Assert.Equal(LegacySeedData.Items, items.Select(LegacySeedData.Item));
    }
}
