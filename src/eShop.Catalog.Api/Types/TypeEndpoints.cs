using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Types;

internal static class TypeEndpoints
{
    internal static IEndpointRouteBuilder MapTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var types = endpoints.MapGroup("/api/types").WithTags("Types");
        types.MapGet("", GetTypesAsync);
        return endpoints;
    }

    /// <summary>Gets every item type.</summary>
    /// <remarks>The types are in ID order.</remarks>
    /// <response code="200">The types.</response>
    internal static async Task<Ok<IReadOnlyList<CatalogTypeResponse>>> GetTypesAsync(ICatalogService service, CancellationToken cancellationToken)
    {
        var types = await service.GetCatalogTypesAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CatalogTypeResponse>>([.. types.Select(CatalogTypeResponse.From)]);
    }
}
