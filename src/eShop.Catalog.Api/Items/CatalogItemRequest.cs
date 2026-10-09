using System.ComponentModel.DataAnnotations;
using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Items;

// Public, because .NET 10's validation skips internal types. A value type is nullable only so that a missing
// value fails [Required] instead of binding as 0 or false.
/// <summary>The fields of an item that a client writes.</summary>
/// <param name="Name">The item's name.</param>
/// <param name="Price">The item's price, with at most two decimal places.</param>
/// <param name="CatalogTypeId">The ID of the item's type.</param>
/// <param name="CatalogBrandId">The ID of the item's brand.</param>
/// <param name="AvailableStock">The quantity in stock.</param>
/// <param name="RestockThreshold">The stock at which the item should be reordered.</param>
/// <param name="MaxStockThreshold">The most units that can be in stock at any time.</param>
/// <param name="OnReorder">Whether the item is on reorder.</param>
/// <param name="Description">The item's description. Optional.</param>
public sealed record CatalogItemRequest(
    [property: Required, StringLength(50)] string? Name,
    [property: Required, Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true), TwoDecimalPlaces] decimal? Price,
    [property: Required] int? CatalogTypeId,
    [property: Required] int? CatalogBrandId,
    [property: Required, Range(0, 10_000_000)] int? AvailableStock,
    [property: Required, Range(0, 10_000_000)] int? RestockThreshold,
    [property: Required, Range(0, 10_000_000)] int? MaxStockThreshold,
    [property: Required] bool? OnReorder,
    string? Description = null)
{
    internal CatalogItemFields ToFields() => new()
    {
        Name = Name!,
        Description = Description,
        Price = Price!.Value,
        CatalogTypeId = CatalogTypeId!.Value,
        CatalogBrandId = CatalogBrandId!.Value,
        AvailableStock = AvailableStock!.Value,
        RestockThreshold = RestockThreshold!.Value,
        MaxStockThreshold = MaxStockThreshold!.Value,
        OnReorder = OnReorder!.Value,
    };
}
