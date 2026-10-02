using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Pictures;

// The legacy PicController, drop-in (ADR-0002, ADR-0023): GET /items/{catalogItemId:int}/pic, under the legacy route
// name, with which the item endpoints of Stage 7.5 build each item's PictureUri.
internal static class PictureEndpoints
{
    // The legacy PicController.GetPicRouteName.
    internal const string RouteName = "GetPicRouteTemplate";

    internal static IEndpointRouteBuilder MapPictureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The int constraint is the legacy route's: an ID that is not an int matches no route, which is a 404.
        endpoints.MapGet("/items/{catalogItemId:int}/pic", GetPictureAsync).WithName(RouteName);
        return endpoints;
    }

    // 400 for an ID below 1 and 404 for an unknown item, as in the legacy app. 404 too for an item whose picture is not
    // a file in the pictures folder, where the legacy app answered 500, or served the file that the name pointed at.
    // A Range request gets the part that it asks for (206).
    private static async Task<Results<PhysicalFileHttpResult, BadRequest, NotFound>> GetPictureAsync(
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
