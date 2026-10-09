using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Catalog;

internal sealed class CatalogService(CatalogDbContext context) : ICatalogService
{
    public async Task<PaginatedItems<CatalogItem>> GetCatalogItemsPaginatedAsync(int pageSize, int pageIndex, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

        var totalItems = await context.CatalogItems.LongCountAsync(cancellationToken);

        // The offset is a long because pageSize * pageIndex overflows an int. Keep the offset < totalItems guard: it
        // stops the checked cast to int from throwing for a page far past the end.
        var offset = (long)pageSize * pageIndex;
        IReadOnlyList<CatalogItem> itemsOnPage = offset < totalItems
            ? await ItemsWithBrandAndType()
                .OrderBy(item => item.Id)
                .Skip(checked((int)offset))
                .Take(pageSize)
                .ToListAsync(cancellationToken)
            : [];

        return new PaginatedItems<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);
    }

    public Task<CatalogItem?> FindCatalogItemAsync(int id, CancellationToken cancellationToken) =>
        ItemsWithBrandAndType().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CatalogBrand>> GetCatalogBrandsAsync(CancellationToken cancellationToken) =>
        await context.CatalogBrands.AsNoTracking().OrderBy(brand => brand.Id).ToListAsync(cancellationToken);

    public Task<CatalogBrand?> FindCatalogBrandAsync(int id, CancellationToken cancellationToken) =>
        context.CatalogBrands.AsNoTracking().FirstOrDefaultAsync(brand => brand.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CatalogType>> GetCatalogTypesAsync(CancellationToken cancellationToken) =>
        await context.CatalogTypes.AsNoTracking().OrderBy(type => type.Id).ToListAsync(cancellationToken);

    public async Task<CatalogItem> CreateCatalogItemAsync(CatalogItemFields fields, CancellationToken cancellationToken)
    {
        var item = new CatalogItem
        {
            Name = fields.Name,
            Description = fields.Description,
            Price = fields.Price,
            PictureFileName = CatalogItem.DefaultPictureFileName,
            CatalogTypeId = fields.CatalogTypeId,
            CatalogBrandId = fields.CatalogBrandId,
            AvailableStock = fields.AvailableStock,
            RestockThreshold = fields.RestockThreshold,
            MaxStockThreshold = fields.MaxStockThreshold,
            OnReorder = fields.OnReorder,
        };

        try
        {
            await context.CatalogItems.AddAsync(item, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.Entry(item).State = EntityState.Detached;
        }

        return item;
    }

    public async Task<bool> UpdateCatalogItemAsync(int id, CatalogItemFields fields, CancellationToken cancellationToken) =>
        await context.CatalogItems
            .Where(item => item.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Name, fields.Name)
                    .SetProperty(item => item.Description, fields.Description)
                    .SetProperty(item => item.Price, fields.Price)
                    .SetProperty(item => item.CatalogTypeId, fields.CatalogTypeId)
                    .SetProperty(item => item.CatalogBrandId, fields.CatalogBrandId)
                    .SetProperty(item => item.AvailableStock, fields.AvailableStock)
                    .SetProperty(item => item.RestockThreshold, fields.RestockThreshold)
                    .SetProperty(item => item.MaxStockThreshold, fields.MaxStockThreshold)
                    .SetProperty(item => item.OnReorder, fields.OnReorder),
                cancellationToken) > 0;

    public async Task<bool> RemoveCatalogItemAsync(int id, CancellationToken cancellationToken) =>
        await context.CatalogItems.Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    private IQueryable<CatalogItem> ItemsWithBrandAndType() =>
        context.CatalogItems.AsNoTracking().Include(item => item.CatalogBrand).Include(item => item.CatalogType);
}
