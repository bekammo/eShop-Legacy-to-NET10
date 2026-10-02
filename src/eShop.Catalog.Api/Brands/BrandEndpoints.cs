using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Brands;

// The legacy Web API 2 BrandsController, drop-in (ADR-0002): the same routes, statuses and JSON (ADR-0020). Deltas
// BC-002 to BC-005 record where the new API answers differently, and why.
internal static class BrandEndpoints
{
    internal static IEndpointRouteBuilder MapBrandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var brands = endpoints.MapGroup("/api/brands");
        brands.MapGet("", GetBrandsAsync);
        brands.MapGet("/{id}", GetBrandAsync);
        brands.MapDelete("/{id}", DeleteBrandAsync);
        return endpoints;
    }

    // Every brand, in ID order.
    private static async Task<Ok<IReadOnlyList<CatalogBrandResponse>>> GetBrandsAsync(ICatalogService service, CancellationToken cancellationToken)
    {
        var brands = await service.GetCatalogBrandsAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CatalogBrandResponse>>([.. brands.Select(CatalogBrandResponse.From)]);
    }

    // {id} has no route constraint, so an ID that is not an int, such as abc, matches the route and fails to bind,
    // which is a 400, as in the legacy app (ADR-0020).
    private static async Task<Results<Ok<CatalogBrandResponse>, NotFound>> GetBrandAsync(int id, ICatalogService service, CancellationToken cancellationToken) =>
        await service.FindCatalogBrandAsync(id, cancellationToken) is { } brand
            ? TypedResults.Ok(CatalogBrandResponse.From(brand))
            : TypedResults.NotFound();

    // Deletes nothing, as the legacy action did ("demo only"): 200 without a body for a brand that exists, 404 for one
    // that does not (ADR-0002, decision 1).
    private static async Task<Results<Ok, NotFound>> DeleteBrandAsync(int id, ICatalogService service, CancellationToken cancellationToken) =>
        await service.FindCatalogBrandAsync(id, cancellationToken) is null
            ? TypedResults.NotFound()
            : TypedResults.Ok();
}
