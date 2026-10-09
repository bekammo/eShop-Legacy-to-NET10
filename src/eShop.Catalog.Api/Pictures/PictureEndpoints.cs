using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Pictures;

internal static class PictureEndpoints
{
    internal const string RouteName = "GetPicRouteTemplate";

    internal static IEndpointRouteBuilder MapPictureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/items/{catalogItemId:int}/pic", GetPictureAsync)
            .WithName(RouteName)
            .WithTags("Pictures")
            .Produces<Stream>(StatusCodes.Status200OK, "image/png")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    /// <summary>Gets an item's picture.</summary>
    /// <remarks>The content type comes from the picture file's extension, such as image/png. A Range request gets the part it asks for (206), and a range outside the file a 416 without a body.</remarks>
    /// <param name="catalogItemId">The item's ID.</param>
    /// <response code="200">The picture.</response>
    /// <response code="400">The ID is below 1.</response>
    /// <response code="404">No item has this ID, the ID is not an integer, or the item's picture is missing.</response>
    internal static async Task<Results<PhysicalFileHttpResult, BadRequest, NotFound>> GetPictureAsync(
        int catalogItemId, ICatalogService service, CatalogPictures pictures, CancellationToken cancellationToken)
    {
        if (catalogItemId <= 0)
        {
            return TypedResults.BadRequest();
        }

        if (await service.FindCatalogItemAsync(catalogItemId, cancellationToken) is not { } item || pictures.Find(item) is not { } picture)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.PhysicalFile(picture.PhysicalPath, picture.ContentType, enableRangeProcessing: true);
    }
}
