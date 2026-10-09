using System.Collections.Concurrent;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Tests.Catalog;
using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.UnitTests.Catalog;

public sealed class InMemoryCatalogServiceTests : CatalogServiceContractTests
{
    private const int Writers = 200;

    private protected override ICatalogService Service { get; } = new InMemoryCatalogService();

    [Fact]
    public async Task Concurrent_writes_and_reads_keep_the_catalog_consistent()
    {
        var writers = new ConcurrentDictionary<int, int>();

        await Parallel.ForAsync(0, Writers, CancellationToken, async (writer, cancellationToken) =>
        {
            var created = await Service.CreateCatalogItemAsync(NewFields($"Created {writer}"), cancellationToken);
            Assert.True(writers.TryAdd(created.Id, writer), $"ID {created.Id} was given twice.");

            var page = await Service.GetCatalogItemsPaginatedAsync(1000, 0, cancellationToken);
            Assert.Contains(created.Id, page.Data.Select(item => item.Id));

            Assert.True(await Service.UpdateCatalogItemAsync(created.Id, NewFields($"Updated {writer}"), cancellationToken));
            Assert.Equal($"Updated {writer}", (await Service.FindCatalogItemAsync(created.Id, cancellationToken))?.Name);
            if (writer % 2 == 0)
            {
                Assert.True(await Service.RemoveCatalogItemAsync(created.Id, cancellationToken));
            }
        });

        var all = await Service.GetCatalogItemsPaginatedAsync(1000, 0, CancellationToken);
        var sampleItems = LegacySeedData.Table("Catalog").Select(row => LegacySeedData.Row((int)row["Id"]!, (string)row["Name"]!));
        var keptItems = writers.Where(pair => pair.Value % 2 == 1).OrderBy(pair => pair.Key).Select(pair => LegacySeedData.Row(pair.Key, $"Updated {pair.Value}"));
        Assert.Equal(12 + (Writers / 2), all.TotalItems);
        Assert.Equal([.. sampleItems, .. keptItems], all.Data.Select(item => LegacySeedData.Row(item.Id, item.Name)));
    }
}
