using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Types;

// A type as the API writes it: {"Id": 1, "Type": "Mug"}, the legacy CatalogType's properties (ADR-0024).
/// <summary>A type of catalog items.</summary>
/// <param name="Id">The type's ID.</param>
/// <param name="Type">The type's name.</param>
internal sealed record CatalogTypeResponse(int Id, string Type)
{
    public static CatalogTypeResponse From(CatalogType type) => new(type.Id, type.Type);
}
