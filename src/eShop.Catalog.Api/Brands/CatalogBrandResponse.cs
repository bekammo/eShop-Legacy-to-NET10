using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Brands;

// A brand as the API writes it: {"Id": 1, "Brand": "Azure"}, the legacy CatalogBrand's JSON (ADR-0020).
/// <summary>A brand of catalog items.</summary>
/// <param name="Id">The brand's ID.</param>
/// <param name="Brand">The brand's name.</param>
internal sealed record CatalogBrandResponse(int Id, string Brand)
{
    public static CatalogBrandResponse From(CatalogBrand brand) => new(brand.Id, brand.Brand);
}
