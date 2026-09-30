namespace eShop.Catalog.Api.Catalog;

// A catalog item as stored. Validation belongs to the request contracts (Stage 7.6), and the
// picture URL is computed per request (Stage 7.5), so neither lives here (ADR-0010).
internal sealed class CatalogItem
{
    // The picture that the service gives every new item, as the legacy CatalogItem constructor did. Clients do not
    // choose pictures (ADR-0015).
    internal const string DefaultPictureFileName = "dummy.png";

    // Assigned by HiLo from the catalog_hilo sequence when the item is added to the context.
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public required string PictureFileName { get; set; }

    public int CatalogTypeId { get; set; }

    // Loaded only when a query includes it.
    public CatalogType? CatalogType { get; set; }

    public int CatalogBrandId { get; set; }

    // Loaded only when a query includes it.
    public CatalogBrand? CatalogBrand { get; set; }

    // Quantity in stock.
    public int AvailableStock { get; set; }

    // Available stock at which the item should be reordered.
    public int RestockThreshold { get; set; }

    // Maximum number of units that can be in stock at any time.
    public int MaxStockThreshold { get; set; }

    public bool OnReorder { get; set; }
}
