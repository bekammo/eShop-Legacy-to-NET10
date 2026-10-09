using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Types;

/// <summary>A type of catalog items.</summary>
/// <param name="Id">The type's ID.</param>
/// <param name="Type">The type's name.</param>
internal sealed record CatalogTypeResponse(int Id, string Type)
{
    public static CatalogTypeResponse From(CatalogType type) => new(type.Id, type.Type);
}
