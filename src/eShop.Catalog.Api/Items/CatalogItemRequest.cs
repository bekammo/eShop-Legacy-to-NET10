using System.ComponentModel.DataAnnotations;
using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Items;

// The fields of an item that a client writes, to create or to replace one (ADR-0025): the legacy form's fields with
// their rules, and OnReorder. Not the ID, which the API assigns, nor the picture (ADR-0015). Every field but the
// description is required: a value type is nullable here only so that a missing value is not read as zero. Public,
// because .NET 10's validation skips internal types (ADR-0020). The rules are on the properties, where the OpenAPI
// document shows them too, and the description has a default, so that the document does not require it.
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
    // The fields, once validation has passed.
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
