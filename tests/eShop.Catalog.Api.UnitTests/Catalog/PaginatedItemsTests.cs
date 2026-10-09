using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.UnitTests.Catalog;

public sealed class PaginatedItemsTests
{
    [Fact]
    public void Page_keeps_what_it_was_given()
    {
        var page = new PaginatedItems<int>(2, 5, 12L, [11, 12]);

        Assert.Equal(2, page.ActualPage);
        Assert.Equal(5, page.ItemsPerPage);
        Assert.Equal(12L, page.TotalItems);
        Assert.Equal([11, 12], page.Data);
    }

    [Theory]
    [InlineData(0L, 10, 0L)]
    [InlineData(1L, 10, 1L)]
    [InlineData(10L, 10, 1L)]
    [InlineData(11L, 10, 2L)]
    [InlineData(12L, 1, 12L)]
    [InlineData(long.MaxValue, 1, long.MaxValue)]
    [InlineData(long.MaxValue, int.MaxValue, 4_294_967_299L)]
    public void Total_pages_hold_every_item(long count, int pageSize, long totalPages) =>
        Assert.Equal(totalPages, new PaginatedItems<int>(0, pageSize, count, []).TotalPages);

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Page_index_below_zero_is_refused(int index) =>
        Assert.Throws<ArgumentOutOfRangeException>("pageIndex", () => new PaginatedItems<int>(index, 10, 0L, []));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Page_size_below_one_is_refused(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>("pageSize", () => new PaginatedItems<int>(0, size, 0L, []));

    [Fact]
    public void Negative_count_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>("count", () => new PaginatedItems<int>(0, 10, -1L, []));

    [Fact]
    public void Missing_data_is_refused() =>
        Assert.Throws<ArgumentNullException>("data", () => new PaginatedItems<int>(0, 10, 0L, null!));

    [Fact]
    public void Page_larger_than_its_size_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>("data", () => new PaginatedItems<int>(0, 2, 3L, [1, 2, 3]));

    [Fact]
    public void Page_may_disagree_with_the_count() =>
        Assert.Equal(3, new PaginatedItems<int>(0, 5, 1L, [1, 2, 3]).Data.Count);
}
