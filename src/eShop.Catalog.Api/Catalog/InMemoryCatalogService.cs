namespace eShop.Catalog.Api.Catalog;

// The catalog in memory, for running without a database (ADR-0016). It starts with the legacy sample data, as the
// legacy CatalogServiceMock did, and loses every change when the process ends. One instance serves every request,
// so a lock guards the items, and callers get copies. The legacy mock shared a plain list and its objects between
// concurrent requests, numbered new items after the highest ID, and stored items with unknown brands (audit D15).
internal sealed class InMemoryCatalogService : ICatalogService
{
    private readonly Lock _lock = new();

    // Reference data, which nothing changes.
    private readonly SortedDictionary<int, CatalogBrand> _brands = new(PreconfiguredData.CatalogBrands().ToDictionary(brand => brand.Id));
    private readonly SortedDictionary<int, CatalogType> _types = new(PreconfiguredData.CatalogTypes().ToDictionary(type => type.Id));

    // The items by ID, so that pages come in ID order. Guarded by _lock, like _lastId.
    private readonly SortedDictionary<int, CatalogItem> _items = [];

    // The last ID given to an item. A removed item's ID is not given again.
    private int _lastId;

    public InMemoryCatalogService()
    {
        // IDs 1-12 in list order, as a new database gives them.
        foreach (var item in PreconfiguredData.CatalogItems())
        {
            item.Id = ++_lastId;
            _items.Add(item.Id, item);
        }
    }

    public Task<PaginatedItems<CatalogItem>> GetCatalogItemsPaginatedAsync(int pageSize, int pageIndex, CancellationToken cancellationToken) =>
        Run(
            () =>
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
                ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

                lock (_lock)
                {
                    // In long, as in CatalogService: pageSize * pageIndex overflows an int.
                    var offset = (long)pageSize * pageIndex;
                    IReadOnlyList<CatalogItem> itemsOnPage = offset < _items.Count
                        ? [.. _items.Values.Skip((int)offset).Take(pageSize).Select(WithBrandAndType)]
                        : [];
                    return new PaginatedItems<CatalogItem>(pageIndex, pageSize, _items.Count, itemsOnPage);
                }
            },
            cancellationToken);

    public Task<CatalogItem?> FindCatalogItemAsync(int id, CancellationToken cancellationToken) =>
        Run(
            () =>
            {
                lock (_lock)
                {
                    return _items.TryGetValue(id, out var item) ? WithBrandAndType(item) : null;
                }
            },
            cancellationToken);

    public Task<IReadOnlyList<CatalogBrand>> GetCatalogBrandsAsync(CancellationToken cancellationToken) =>
        Run<IReadOnlyList<CatalogBrand>>(() => [.. _brands.Values.Select(Copy)], cancellationToken);

    public Task<CatalogBrand?> FindCatalogBrandAsync(int id, CancellationToken cancellationToken) =>
        Run(() => _brands.TryGetValue(id, out var brand) ? Copy(brand) : null, cancellationToken);

    public Task<IReadOnlyList<CatalogType>> GetCatalogTypesAsync(CancellationToken cancellationToken) =>
        Run<IReadOnlyList<CatalogType>>(() => [.. _types.Values.Select(Copy)], cancellationToken);

    public Task<CatalogItem> CreateCatalogItemAsync(CatalogItemFields fields, CancellationToken cancellationToken) =>
        Run(
            () =>
            {
                ThrowIfUnknownBrandOrType(fields);
                var item = new CatalogItem
                {
                    Name = fields.Name,
                    PictureFileName = CatalogItem.DefaultPictureFileName,
                };
                Apply(fields, item);

                // Copied under the lock: once added, the item is shared, and an update may change it.
                lock (_lock)
                {
                    item.Id = checked(++_lastId);
                    _items.Add(item.Id, item);
                    return Copy(item);
                }
            },
            cancellationToken);

    // As in CatalogService, an unknown ID wins over an unknown brand or type: the UPDATE matches no row, so no
    // foreign key is checked.
    public Task<bool> UpdateCatalogItemAsync(int id, CatalogItemFields fields, CancellationToken cancellationToken) =>
        Run(
            () =>
            {
                lock (_lock)
                {
                    if (!_items.TryGetValue(id, out var item))
                    {
                        return false;
                    }

                    ThrowIfUnknownBrandOrType(fields);
                    Apply(fields, item);
                    return true;
                }
            },
            cancellationToken);

    public Task<bool> RemoveCatalogItemAsync(int id, CancellationToken cancellationToken) =>
        Run(
            () =>
            {
                lock (_lock)
                {
                    return _items.Remove(id);
                }
            },
            cancellationToken);

    // Runs an operation the way the asynchronous methods of CatalogService end: a token that is already cancelled
    // stops it before it starts, and its exception is in the task that it returns.
    private static Task<T> Run<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        try
        {
            return Task.FromResult(operation());
        }
        catch (Exception exception)
        {
            return Task.FromException<T>(exception);
        }
    }

    // The database refuses such an item with its foreign keys.
    private void ThrowIfUnknownBrandOrType(CatalogItemFields fields)
    {
        if (!_brands.ContainsKey(fields.CatalogBrandId))
        {
            throw new ArgumentException($"No catalog brand has the ID {fields.CatalogBrandId}.", nameof(fields));
        }

        if (!_types.ContainsKey(fields.CatalogTypeId))
        {
            throw new ArgumentException($"No catalog type has the ID {fields.CatalogTypeId}.", nameof(fields));
        }
    }

    // The same fields that CatalogService writes.
    private static void Apply(CatalogItemFields fields, CatalogItem item)
    {
        item.Name = fields.Name;
        item.Description = fields.Description;
        item.Price = AsStored(fields.Price);
        item.CatalogTypeId = fields.CatalogTypeId;
        item.CatalogBrandId = fields.CatalogBrandId;
        item.AvailableStock = fields.AvailableStock;
        item.RestockThreshold = fields.RestockThreshold;
        item.MaxStockThreshold = fields.MaxStockThreshold;
        item.OnReorder = fields.OnReorder;
    }

    // As the decimal(18,2) column holds a price: one with at most two decimal places comes back with exactly two, so
    // 8 reads as 8.00. The API accepts no more than two (Stage 7.6).
    private static decimal AsStored(decimal price) => decimal.Round(price + 0.00m, 2);

    private CatalogItem WithBrandAndType(CatalogItem item)
    {
        var copy = Copy(item);
        copy.CatalogBrand = Copy(_brands[item.CatalogBrandId]);
        copy.CatalogType = Copy(_types[item.CatalogTypeId]);
        return copy;
    }

    private static CatalogItem Copy(CatalogItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Description = item.Description,
        Price = item.Price,
        PictureFileName = item.PictureFileName,
        CatalogTypeId = item.CatalogTypeId,
        CatalogBrandId = item.CatalogBrandId,
        AvailableStock = item.AvailableStock,
        RestockThreshold = item.RestockThreshold,
        MaxStockThreshold = item.MaxStockThreshold,
        OnReorder = item.OnReorder,
    };

    private static CatalogBrand Copy(CatalogBrand brand) => new() { Id = brand.Id, Brand = brand.Brand };

    private static CatalogType Copy(CatalogType type) => new() { Id = type.Id, Type = type.Type };
}
