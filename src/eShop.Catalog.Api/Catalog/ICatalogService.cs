namespace eShop.Catalog.Api.Catalog;

// The catalog operations behind the endpoints (ADR-0015). They are the legacy service's operations, made
// asynchronous, plus a lookup of one brand (audit D17). Each takes the caller's CancellationToken and passes it on to
// the store. The service is not IDisposable, because the container owns whatever it uses. What it returns belongs to
// the caller, who can change it without changing the catalog.
internal interface ICatalogService
{
    // One page of items in ID order, each with its brand and type. pageSize must be at least 1 and pageIndex at
    // least 0. A page after the last one is empty.
    Task<PaginatedItems<CatalogItem>> GetCatalogItemsPaginatedAsync(int pageSize, int pageIndex, CancellationToken cancellationToken);

    // The item with its brand and type, or null when no item has this ID.
    Task<CatalogItem?> FindCatalogItemAsync(int id, CancellationToken cancellationToken);

    // Every brand, in ID order.
    Task<IReadOnlyList<CatalogBrand>> GetCatalogBrandsAsync(CancellationToken cancellationToken);

    // The brand, or null when no brand has this ID.
    Task<CatalogBrand?> FindCatalogBrandAsync(int id, CancellationToken cancellationToken);

    // Every type, in ID order.
    Task<IReadOnlyList<CatalogType>> GetCatalogTypesAsync(CancellationToken cancellationToken);

    // Adds an item with a new ID and the default picture, and returns it without its brand and type. An unknown
    // brand or type is refused with an exception, and nothing is added.
    Task<CatalogItem> CreateCatalogItemAsync(CatalogItemFields fields, CancellationToken cancellationToken);

    // Writes every field to the item, which keeps its ID and picture. False when no item has this ID, whatever the
    // fields hold. For a known item, an unknown brand or type is refused with an exception, and nothing is changed.
    Task<bool> UpdateCatalogItemAsync(int id, CatalogItemFields fields, CancellationToken cancellationToken);

    // Deletes the item. False when no item has this ID.
    Task<bool> RemoveCatalogItemAsync(int id, CancellationToken cancellationToken);
}
