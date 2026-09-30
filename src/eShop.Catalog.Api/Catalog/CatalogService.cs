using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Catalog;

// The catalog in SQL Server, through EF Core (ADR-0015). It works on the scoped CatalogDbContext, so it is scoped
// too. Reads do not track, and a write leaves nothing tracked, so no later SaveChanges on the context writes back a
// change that a caller makes to a returned item.
internal sealed class CatalogService(CatalogDbContext context) : ICatalogService
{
    public async Task<PaginatedItems<CatalogItem>> GetCatalogItemsPaginatedAsync(int pageSize, int pageIndex, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

        var totalItems = await context.CatalogItems.LongCountAsync(cancellationToken);

        // pageSize * pageIndex overflows an int (audit D6), so the offset is a long. A page that starts after the
        // last item is empty without the page query, so an offset that reaches Skip is below the count. It fits an
        // int unless the table holds more than int.MaxValue rows, and then the checked cast throws.
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

    // One query for the one brand. The legacy controller read every brand and searched them in memory (audit D17).
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
            // HiLo assigns the ID as the item is added. AddAsync lets it draw a new block from the sequence
            // without blocking.
            await context.CatalogItems.AddAsync(item, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // Saved or refused, the item is not left for a later SaveChanges to write.
            context.Entry(item).State = EntityState.Detached;
        }

        return item;
    }

    // One UPDATE that sets exactly these fields. The ID and the picture are not among them.
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
