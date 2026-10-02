using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Types;

// A type as the API writes it: {"Id": 1, "Type": "Mug"}, the legacy CatalogType's properties (ADR-0024).
internal sealed record CatalogTypeResponse(int Id, string Type)
{
    public static CatalogTypeResponse From(CatalogType type) => new(type.Id, type.Type);
}
