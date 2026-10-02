using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Types;

// The item types, which the legacy app offered only as a dropdown of its Razor forms (ADR-0001, ADR-0024).
internal static class TypeEndpoints
{
    internal static IEndpointRouteBuilder MapTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var types = endpoints.MapGroup("/api/types");
        types.MapGet("", GetTypesAsync);
        return endpoints;
    }

    // Every type, in ID order.
    private static async Task<Ok<IReadOnlyList<CatalogTypeResponse>>> GetTypesAsync(ICatalogService service, CancellationToken cancellationToken)
    {
        var types = await service.GetCatalogTypesAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CatalogTypeResponse>>([.. types.Select(CatalogTypeResponse.From)]);
    }
}
