namespace eShop.Catalog.Api.Catalog;

internal interface ICatalogService
{
    Task<PaginatedItems<CatalogItem>> GetCatalogItemsPaginatedAsync(int pageSize, int pageIndex, CancellationToken cancellationToken);

    Task<CatalogItem?> FindCatalogItemAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogBrand>> GetCatalogBrandsAsync(CancellationToken cancellationToken);

    Task<CatalogBrand?> FindCatalogBrandAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogType>> GetCatalogTypesAsync(CancellationToken cancellationToken);

    Task<CatalogItem> CreateCatalogItemAsync(CatalogItemFields fields, CancellationToken cancellationToken);

    Task<bool> UpdateCatalogItemAsync(int id, CatalogItemFields fields, CancellationToken cancellationToken);

    Task<bool> RemoveCatalogItemAsync(int id, CancellationToken cancellationToken);
}
