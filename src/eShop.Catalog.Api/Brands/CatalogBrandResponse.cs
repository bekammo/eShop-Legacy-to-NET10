using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Brands;

// A brand as the API writes it: {"Id": 1, "Brand": "Azure"}, the legacy CatalogBrand's JSON (ADR-0020).
internal sealed record CatalogBrandResponse(int Id, string Brand)
{
    public static CatalogBrandResponse From(CatalogBrand brand) => new(brand.Id, brand.Brand);
}
