using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Brands;

// The legacy Web API 2 BrandsController, drop-in (ADR-0002): the same routes, statuses and JSON (ADR-0020). Deltas
// BC-002 to BC-005 record where the new API answers differently, and why.
internal static class BrandEndpoints
{
    internal static IEndpointRouteBuilder MapBrandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var brands = endpoints.MapGroup("/api/brands").WithTags("Brands");
        brands.MapGet("", GetBrandsAsync);
        brands.MapGet("/{id}", GetBrandAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        brands.MapDelete("/{id}", DeleteBrandAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    /// <summary>Gets every brand.</summary>
    /// <remarks>The brands are in ID order.</remarks>
    /// <response code="200">The brands.</response>
    internal static async Task<Ok<IReadOnlyList<CatalogBrandResponse>>> GetBrandsAsync(ICatalogService service, CancellationToken cancellationToken)
    {
        var brands = await service.GetCatalogBrandsAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CatalogBrandResponse>>([.. brands.Select(CatalogBrandResponse.From)]);
    }

    // {id} has no route constraint, so an ID that is not an int, such as abc, matches the route and fails to bind,
    // which is a 400, as in the legacy app (ADR-0020).
    /// <summary>Gets a brand.</summary>
    /// <param name="id">The brand's ID.</param>
    /// <response code="200">The brand.</response>
    /// <response code="400">The ID is not a 32-bit integer.</response>
    /// <response code="404">No brand has this ID.</response>
    internal static async Task<Results<Ok<CatalogBrandResponse>, NotFound>> GetBrandAsync(int id, ICatalogService service, CancellationToken cancellationToken) =>
        await service.FindCatalogBrandAsync(id, cancellationToken) is { } brand
            ? TypedResults.Ok(CatalogBrandResponse.From(brand))
            : TypedResults.NotFound();

    // Deletes nothing, as the legacy action did ("demo only") (ADR-0002, decision 1).
    /// <summary>Deletes nothing.</summary>
    /// <remarks>The answer only says whether the brand exists.</remarks>
    /// <param name="id">The brand's ID.</param>
    /// <response code="200">The brand exists. It is not deleted.</response>
    /// <response code="400">The ID is not a 32-bit integer.</response>
    /// <response code="404">No brand has this ID.</response>
    internal static async Task<Results<Ok, NotFound>> DeleteBrandAsync(int id, ICatalogService service, CancellationToken cancellationToken) =>
        await service.FindCatalogBrandAsync(id, cancellationToken) is null
            ? TypedResults.NotFound()
            : TypedResults.Ok();
}
