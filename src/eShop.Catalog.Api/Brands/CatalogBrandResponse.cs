using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Brands;

/// <summary>A brand of catalog items.</summary>
/// <param name="Id">The brand's ID.</param>
/// <param name="Brand">The brand's name.</param>
internal sealed record CatalogBrandResponse(int Id, string Brand)
{
    public static CatalogBrandResponse From(CatalogBrand brand) => new(brand.Id, brand.Brand);
}
