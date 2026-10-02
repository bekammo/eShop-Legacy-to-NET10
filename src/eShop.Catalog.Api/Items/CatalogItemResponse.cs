using eShop.Catalog.Api.Brands;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Types;

namespace eShop.Catalog.Api.Items;

// An item as the API writes it (ADR-0024): the properties of the legacy CatalogItem model, with its brand and type as
// objects, as the model's navigation properties held them. The item must have them loaded. PictureUri is the absolute URL of the item's picture, as the
// legacy controller built it. The picture's file name stays inside the API.
internal sealed record CatalogItemResponse(
    int Id,
    string Name,
    string? Description,
    decimal Price,
    string PictureUri,
    int CatalogTypeId,
    CatalogTypeResponse CatalogType,
    int CatalogBrandId,
    CatalogBrandResponse CatalogBrand,
    int AvailableStock,
    int RestockThreshold,
    int MaxStockThreshold,
    bool OnReorder)
{
    public static CatalogItemResponse From(CatalogItem item, string pictureUri) => new(
        item.Id,
        item.Name,
        item.Description,
        item.Price,
        pictureUri,
        item.CatalogTypeId,
        CatalogTypeResponse.From(item.CatalogType!),
        item.CatalogBrandId,
        CatalogBrandResponse.From(item.CatalogBrand!),
        item.AvailableStock,
        item.RestockThreshold,
        item.MaxStockThreshold,
        item.OnReorder);
}
