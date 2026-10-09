using eShop.Catalog.Api.Brands;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Types;

namespace eShop.Catalog.Api.Items;

/// <summary>A catalog item.</summary>
/// <param name="Id">The item's ID.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Description">The item's description, or null.</param>
/// <param name="Price">The item's price.</param>
/// <param name="PictureUri">The absolute URL of the item's picture.</param>
/// <param name="CatalogTypeId">The ID of the item's type.</param>
/// <param name="CatalogType">The item's type.</param>
/// <param name="CatalogBrandId">The ID of the item's brand.</param>
/// <param name="CatalogBrand">The item's brand.</param>
/// <param name="AvailableStock">The quantity in stock.</param>
/// <param name="RestockThreshold">The stock at which the item should be reordered.</param>
/// <param name="MaxStockThreshold">The most units that can be in stock at any time.</param>
/// <param name="OnReorder">Whether the item is on reorder.</param>
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
