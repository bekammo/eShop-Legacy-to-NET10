namespace eShop.Catalog.Api.Catalog;

/// <summary>A page of a list.</summary>
internal sealed class PaginatedItems<T>
{
    public PaginatedItems(int pageIndex, int pageSize, long count, IReadOnlyList<T> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(data);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Count, pageSize, nameof(data));

        ActualPage = pageIndex;
        ItemsPerPage = pageSize;
        TotalItems = count;
        Data = data;
    }

    /// <summary>The index of this page, from 0.</summary>
    public int ActualPage { get; }

    /// <summary>The page size: the most items that a page holds.</summary>
    public int ItemsPerPage { get; }

    /// <summary>The number of items in the whole list.</summary>
    public long TotalItems { get; }

    /// <summary>The number of pages that hold the whole list, 0 for an empty list.</summary>
    public long TotalPages => TotalItems / ItemsPerPage + (TotalItems % ItemsPerPage == 0 ? 0 : 1);

    /// <summary>The items of this page.</summary>
    public IReadOnlyList<T> Data { get; }
}
