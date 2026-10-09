namespace eShop.Catalog.Api.Catalog;

internal sealed class InMemoryCatalogService : ICatalogService
{
    private readonly Lock _lock = new();

    private readonly SortedDictionary<int, CatalogBrand> _brands = new(PreconfiguredData.CatalogBrands().ToDictionary(brand => brand.Id));
    private readonly SortedDictionary<int, CatalogType> _types = new(PreconfiguredData.CatalogTypes().ToDictionary(type => type.Id));

    // Guarded by _lock. Updates change stored items in place, so copy an item only while holding the lock.
    private readonly SortedDictionary<int, CatalogItem> _items = [];

    private int _lastId;

    public InMemoryCatalogService()
    {
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

                lock (_lock)
                {
                    item.Id = checked(++_lastId);
                    _items.Add(item.Id, item);
                    return Copy(item);
                }
            },
            cancellationToken);

    // Keep the ID lookup before the brand and type check: an unknown ID must return false even with an unknown brand
    // or type, as CatalogService's UPDATE does by matching no row.
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

    // Adding 0.00m is not a no-op: it gives the price two decimal places, so 8 reads as 8.00, as from the
    // decimal(18,2) column in database mode.
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
