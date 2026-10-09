using System.Globalization;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.Tests.Catalog;

public abstract class CatalogServiceContractTests
{
    private static readonly IReadOnlyList<int> SampleItemIds = [.. LegacySeedData.Table("Catalog").Select(row => (int)row["Id"]!)];

    private protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private protected abstract ICatalogService Service { get; }

    [Fact]
    public async Task Brands_are_the_legacy_brands_in_id_order()
    {
        var brands = await Service.GetCatalogBrandsAsync(CancellationToken);

        Assert.Equal(LegacySeedData.Brands, brands.Select(brand => LegacySeedData.Row(brand.Id, brand.Brand)));
    }

    [Fact]
    public async Task Types_are_the_legacy_types_in_id_order()
    {
        var types = await Service.GetCatalogTypesAsync(CancellationToken);

        Assert.Equal(LegacySeedData.Types, types.Select(type => LegacySeedData.Row(type.Id, type.Type)));
    }

    [Fact]
    public async Task Each_brand_is_found_by_its_id()
    {
        var found = new List<string>();
        foreach (var row in LegacySeedData.Table("CatalogBrand"))
        {
            var brand = await Service.FindCatalogBrandAsync((int)row["Id"]!, CancellationToken);
            found.Add(brand is null ? "(not found)" : LegacySeedData.Row(brand.Id, brand.Brand));
        }

        Assert.Equal(LegacySeedData.Brands, found);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public async Task Unknown_brand_is_not_found(int id) =>
        Assert.Null(await Service.FindCatalogBrandAsync(id, CancellationToken));

    [Theory]
    [InlineData(10, 0, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 2L)]
    [InlineData(10, 1, new[] { 11, 12 }, 2L)]
    [InlineData(4, 2, new[] { 9, 10, 11, 12 }, 3L)]
    [InlineData(12, 0, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, 1L)]
    [InlineData(100, 0, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, 1L)]
    [InlineData(1, 11, new[] { 12 }, 12L)]
    [InlineData(10, 2, new int[] { }, 2L)]
    [InlineData(100, int.MaxValue, new int[] { }, 1L)]
    public async Task Page_holds_the_items_at_its_place_in_id_order(int pageSize, int pageIndex, int[] ids, long totalPages)
    {
        var page = await Service.GetCatalogItemsPaginatedAsync(pageSize, pageIndex, CancellationToken);

        Assert.Equal(pageIndex, page.ActualPage);
        Assert.Equal(pageSize, page.ItemsPerPage);
        Assert.Equal(12L, page.TotalItems);
        Assert.Equal(totalPages, page.TotalPages);
        Assert.Equal(ids.Select(SampleItem), page.Data.Select(LegacySeedData.Item));
    }

    [Fact]
    public async Task Paged_items_come_with_their_brands_and_types()
    {
        var page = await Service.GetCatalogItemsPaginatedAsync(12, 0, CancellationToken);

        Assert.Equal(LegacySeedData.ItemsWithBrandAndType, page.Data.Select(LegacySeedData.ItemWithBrandAndType));
    }

    [Theory]
    [InlineData(0, 0, "pageSize")]
    [InlineData(-1, 0, "pageSize")]
    [InlineData(int.MinValue, 0, "pageSize")]
    [InlineData(10, -1, "pageIndex")]
    [InlineData(10, int.MinValue, "pageIndex")]
    public async Task Invalid_paging_is_refused(int pageSize, int pageIndex, string parameter) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            parameter,
            () => Service.GetCatalogItemsPaginatedAsync(pageSize, pageIndex, CancellationToken));

    [Fact]
    public async Task Each_item_is_found_by_its_id_with_its_brand_and_type()
    {
        var found = new List<CatalogItem?>();
        foreach (var id in SampleItemIds)
        {
            found.Add(await Service.FindCatalogItemAsync(id, CancellationToken));
        }

        Assert.Equal(LegacySeedData.Items, found.Select(item => item is null ? "(not found)" : LegacySeedData.Item(item)));
        Assert.Equal(LegacySeedData.ItemsWithBrandAndType, found.Select(item => item is null ? "(not found)" : LegacySeedData.ItemWithBrandAndType(item)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(13)]
    [InlineData(int.MaxValue)]
    public async Task Unknown_item_is_not_found(int id) =>
        Assert.Null(await Service.FindCatalogItemAsync(id, CancellationToken));

    [Fact]
    public async Task Created_item_gets_a_new_id_the_given_fields_and_the_default_picture()
    {
        var fields = NewFields("Created");

        var created = await Service.CreateCatalogItemAsync(fields, CancellationToken);

        Assert.True(created.Id > 0);
        Assert.DoesNotContain(created.Id, SampleItemIds);
        var expected = ItemLine(created.Id, fields, "dummy.png");
        Assert.Equal(expected, LegacySeedData.Item(created));
        Assert.Null(created.CatalogBrand);
        Assert.Null(created.CatalogType);

        var stored = await Service.FindCatalogItemAsync(created.Id, CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(expected, LegacySeedData.Item(stored));
        Assert.Equal("Created | SQL Server | USB Memory Stick", LegacySeedData.ItemWithBrandAndType(stored));
        Assert.Equal([.. LegacySeedData.Items, expected], await AllItemsAsync());
    }

    [Fact]
    public async Task Ids_are_never_given_again()
    {
        var first = await Service.CreateCatalogItemAsync(NewFields("First"), CancellationToken);
        Assert.True(await Service.RemoveCatalogItemAsync(first.Id, CancellationToken));
        Assert.True(await Service.RemoveCatalogItemAsync(12, CancellationToken));

        var second = await Service.CreateCatalogItemAsync(NewFields("Second"), CancellationToken);

        Assert.DoesNotContain(second.Id, SampleItemIds.Append(first.Id));
    }

    [Fact]
    public async Task Update_writes_every_field_and_keeps_the_id_and_the_picture()
    {
        var fields = NewFields("Updated") with { Description = null };

        Assert.True(await Service.UpdateCatalogItemAsync(3, fields, CancellationToken));

        var expected = ItemLine(3, fields, "3.png");
        Assert.Equal(LegacySeedData.Items.Select(line => line == SampleItem(3) ? expected : line), await AllItemsAsync());
        var updated = await Service.FindCatalogItemAsync(3, CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("Updated | SQL Server | USB Memory Stick", LegacySeedData.ItemWithBrandAndType(updated));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(int.MaxValue)]
    public async Task Update_of_an_unknown_item_changes_nothing(int id)
    {
        Assert.False(await Service.UpdateCatalogItemAsync(id, NewFields("Updated"), CancellationToken));

        Assert.Equal(LegacySeedData.Items, await AllItemsAsync());
    }

    [Fact]
    public async Task Update_of_an_unknown_item_with_an_unknown_brand_returns_false()
    {
        var fields = NewFields("Updated") with { CatalogBrandId = 6, CatalogTypeId = 5 };

        Assert.False(await Service.UpdateCatalogItemAsync(13, fields, CancellationToken));

        Assert.Equal(LegacySeedData.Items, await AllItemsAsync());
    }

    [Fact]
    public async Task Prices_are_read_back_with_two_decimal_places()
    {
        var created = await Service.CreateCatalogItemAsync(NewFields("Created") with { Price = 8m }, CancellationToken);
        Assert.True(await Service.UpdateCatalogItemAsync(1, NewFields("Updated") with { Price = 8.5m }, CancellationToken));

        var page = await Service.GetCatalogItemsPaginatedAsync(100, 0, CancellationToken);
        Assert.Equal("8.00", Price(page.Data.Single(item => item.Id == created.Id)));
        Assert.Equal("8.50", Price(page.Data.Single(item => item.Id == 1)));
        Assert.Equal("8.00", Price((await Service.FindCatalogItemAsync(created.Id, CancellationToken))!));

        static string Price(CatalogItem item) => item.Price.ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Emptied_catalog_takes_new_items()
    {
        foreach (var id in SampleItemIds)
        {
            Assert.True(await Service.RemoveCatalogItemAsync(id, CancellationToken));
        }

        var empty = await Service.GetCatalogItemsPaginatedAsync(10, 0, CancellationToken);
        Assert.Equal(0L, empty.TotalItems);
        Assert.Equal(0L, empty.TotalPages);
        Assert.Empty(empty.Data);

        var created = await Service.CreateCatalogItemAsync(NewFields("Created"), CancellationToken);

        Assert.DoesNotContain(created.Id, SampleItemIds);
        Assert.Equal([ItemLine(created.Id, NewFields("Created"), "dummy.png")], await AllItemsAsync());
    }

    [Fact]
    public async Task Pages_stay_in_id_order_after_removes_and_creates()
    {
        Assert.True(await Service.RemoveCatalogItemAsync(1, CancellationToken));
        var created = await Service.CreateCatalogItemAsync(NewFields("Created"), CancellationToken);

        Assert.Equal(
            [.. LegacySeedData.Items.Skip(1), ItemLine(created.Id, NewFields("Created"), "dummy.png")],
            await AllItemsAsync());
    }

    [Fact]
    public async Task Removed_item_is_gone()
    {
        Assert.True(await Service.RemoveCatalogItemAsync(5, CancellationToken));

        Assert.Null(await Service.FindCatalogItemAsync(5, CancellationToken));
        Assert.Equal(LegacySeedData.Items.Where(line => line != SampleItem(5)), await AllItemsAsync());
        Assert.False(await Service.RemoveCatalogItemAsync(5, CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(int.MaxValue)]
    public async Task Removing_an_unknown_item_changes_nothing(int id)
    {
        Assert.False(await Service.RemoveCatalogItemAsync(id, CancellationToken));

        Assert.Equal(LegacySeedData.Items, await AllItemsAsync());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(6, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 5)]
    public async Task Item_with_an_unknown_brand_or_type_is_refused(int catalogBrandId, int catalogTypeId)
    {
        var fields = NewFields("Refused") with { CatalogBrandId = catalogBrandId, CatalogTypeId = catalogTypeId };

        await Assert.ThrowsAnyAsync<Exception>(() => Service.CreateCatalogItemAsync(fields, CancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(() => Service.UpdateCatalogItemAsync(1, fields, CancellationToken));

        Assert.Equal(LegacySeedData.Items, await AllItemsAsync());
        Assert.Equal(LegacySeedData.ItemsWithBrandAndType, (await Service.GetCatalogItemsPaginatedAsync(12, 0, CancellationToken)).Data.Select(LegacySeedData.ItemWithBrandAndType));
    }

    [Fact]
    public async Task Cancelled_operations_change_nothing()
    {
        var cancelled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.GetCatalogItemsPaginatedAsync(10, 0, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.FindCatalogItemAsync(1, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.GetCatalogBrandsAsync(cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.FindCatalogBrandAsync(1, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.GetCatalogTypesAsync(cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CreateCatalogItemAsync(NewFields("Cancelled"), cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.UpdateCatalogItemAsync(1, NewFields("Cancelled"), cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.RemoveCatalogItemAsync(1, cancelled));

        Assert.Equal(LegacySeedData.Items, await AllItemsAsync());
    }

    [Fact]
    public async Task Changing_what_the_service_returned_changes_nothing()
    {
        var created = await Service.CreateCatalogItemAsync(NewFields("Created"), CancellationToken);
        var expected = ItemLine(created.Id, NewFields("Created"), "dummy.png");
        created.Name = "Changed";
        created.PictureFileName = "changed.png";

        foreach (var item in (await Service.GetCatalogItemsPaginatedAsync(100, 0, CancellationToken)).Data)
        {
            Change(item);
        }

        Change((await Service.FindCatalogItemAsync(1, CancellationToken))!);
        foreach (var brand in await Service.GetCatalogBrandsAsync(CancellationToken))
        {
            brand.Brand = "Changed";
        }

        foreach (var type in await Service.GetCatalogTypesAsync(CancellationToken))
        {
            type.Type = "Changed";
        }

        (await Service.FindCatalogBrandAsync(2, CancellationToken))!.Brand = "Changed";
        var later = await Service.CreateCatalogItemAsync(NewFields("Later"), CancellationToken);

        Assert.Equal([.. LegacySeedData.Items, expected, ItemLine(later.Id, NewFields("Later"), "dummy.png")], await AllItemsAsync());
        Assert.Equal(LegacySeedData.Brands, (await Service.GetCatalogBrandsAsync(CancellationToken)).Select(brand => LegacySeedData.Row(brand.Id, brand.Brand)));
        Assert.Equal(LegacySeedData.Types, (await Service.GetCatalogTypesAsync(CancellationToken)).Select(type => LegacySeedData.Row(type.Id, type.Type)));
        Assert.Equal(LegacySeedData.ItemsWithBrandAndType, (await Service.GetCatalogItemsPaginatedAsync(12, 0, CancellationToken)).Data.Select(LegacySeedData.ItemWithBrandAndType));

        static void Change(CatalogItem item)
        {
            item.Name = "Changed";
            item.Price = 0;
            item.PictureFileName = "changed.png";
            item.CatalogBrand!.Brand = "Changed";
            item.CatalogType!.Type = "Changed";
        }
    }

    private protected static CatalogItemFields NewFields(string name) => new()
    {
        Name = name,
        Description = name + " description",
        Price = 7.25m,
        CatalogTypeId = 4,
        CatalogBrandId = 4,
        AvailableStock = 50,
        RestockThreshold = 5,
        MaxStockThreshold = 200,
        OnReorder = true,
    };

    private static string ItemLine(int id, CatalogItemFields fields, string pictureFileName) => LegacySeedData.Item(
        id, fields.Name, fields.Description, fields.Price, pictureFileName, fields.CatalogTypeId, fields.CatalogBrandId,
        fields.AvailableStock, fields.RestockThreshold, fields.MaxStockThreshold, fields.OnReorder);

    private static string SampleItem(int id) => LegacySeedData.Items[id - 1];

    private async Task<IReadOnlyList<string>> AllItemsAsync()
    {
        var page = await Service.GetCatalogItemsPaginatedAsync(100, 0, CancellationToken);
        Assert.Equal(page.Data.Count, page.TotalItems);
        return [.. page.Data.Select(LegacySeedData.Item)];
    }
}
