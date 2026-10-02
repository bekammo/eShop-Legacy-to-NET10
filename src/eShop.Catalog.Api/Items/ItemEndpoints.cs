using System.ComponentModel.DataAnnotations;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Pictures;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Items;

// The catalog items, which the legacy app offered only through its Razor UI, as REST endpoints (ADR-0001, ADR-0024).
internal static class ItemEndpoints
{
    internal static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var items = endpoints.MapGroup("/api/items");
        items.MapGet("", GetItemsAsync);
        items.MapGet("/{id}", GetItemAsync);
        return endpoints;
    }

    // One page of items, in ID order, with the legacy Index action's defaults: page 0 of 10 items. A page size outside
    // 1-100 or a negative page index is a 400 (audit D6). A page after the last one is empty.
    private static async Task<Ok<PaginatedItems<CatalogItemResponse>>> GetItemsAsync(
        ICatalogService service,
        LinkGenerator links,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [Range(1, 100)] int pageSize = 10,
        [Range(0, int.MaxValue)] int pageIndex = 0)
    {
        var page = await service.GetCatalogItemsPaginatedAsync(pageSize, pageIndex, cancellationToken);
        return TypedResults.Ok(new PaginatedItems<CatalogItemResponse>(
            page.ActualPage, page.ItemsPerPage, page.TotalItems, [.. page.Data.Select(item => Response(item, links, httpContext))]));
    }

    // {id} has no route constraint, so an ID that is not an int is a 400, as the legacy Details action answered.
    private static async Task<Results<Ok<CatalogItemResponse>, NotFound>> GetItemAsync(
        int id, ICatalogService service, LinkGenerator links, HttpContext httpContext, CancellationToken cancellationToken) =>
        await service.FindCatalogItemAsync(id, cancellationToken) is { } item
            ? TypedResults.Ok(Response(item, links, httpContext))
            : TypedResults.NotFound();

    // The item, with the absolute URL of its picture from the picture route's name, as the legacy controller built it.
    private static CatalogItemResponse Response(CatalogItem item, LinkGenerator links, HttpContext httpContext) =>
        CatalogItemResponse.From(item, links.GetUriByName(httpContext, PictureEndpoints.RouteName, new { catalogItemId = item.Id })!);
}
