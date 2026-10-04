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
        var items = endpoints.MapGroup("/api/items").WithTags("Items");
        items.MapGet("", GetItemsAsync)
            .ProducesValidationProblem();
        items.MapGet("/{id}", GetItemAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        items.MapPost("", CreateItemAsync);
        items.MapPut("/{id}", UpdateItemAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        items.MapDelete("/{id}", DeleteItemAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    // The legacy Index action's defaults: page 0 of 10 items. It did not check either value (audit D6).
    /// <summary>Gets a page of items.</summary>
    /// <remarks>The items are in ID order, each with its brand, its type and the URL of its picture. A page after the last is empty.</remarks>
    /// <param name="pageSize">The number of items on a page, from 1 to 100.</param>
    /// <param name="pageIndex">The index of the page, from 0.</param>
    /// <response code="200">The page.</response>
    /// <response code="400">The page size or the page index is out of range, or not an integer.</response>
    internal static async Task<Ok<PaginatedItems<CatalogItemResponse>>> GetItemsAsync(
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
    /// <summary>Gets an item.</summary>
    /// <param name="id">The item's ID.</param>
    /// <response code="200">The item, with its brand, its type and the URL of its picture.</response>
    /// <response code="400">The ID is not a 32-bit integer.</response>
    /// <response code="404">No item has this ID.</response>
    internal static async Task<Results<Ok<CatalogItemResponse>, NotFound>> GetItemAsync(
        int id, ICatalogService service, LinkGenerator links, HttpContext httpContext, CancellationToken cancellationToken) =>
        await service.FindCatalogItemAsync(id, cancellationToken) is { } item
            ? TypedResults.Ok(Response(item, links, httpContext))
            : TypedResults.NotFound();

    // The body is validated before the handler runs (ADR-0025). An unknown brand or type is a 400 too, where the legacy
    // app answered 500 (audit D10).
    /// <summary>Creates an item.</summary>
    /// <remarks>The API gives the item its ID, and the default picture.</remarks>
    /// <param name="request">The item's fields.</param>
    /// <response code="201">The item, as GET /api/items/{id} gives it. The Location header holds its URL.</response>
    /// <response code="400">The body is missing or not an item's JSON, a field breaks its rule, or no brand or type has the ID given. The errors name each field at fault.</response>
    internal static async Task<Results<Created<CatalogItemResponse>, ValidationProblem>> CreateItemAsync(
        CatalogItemRequest request, ICatalogService service, LinkGenerator links, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (await UnknownBrandOrTypeAsync(request, service, cancellationToken) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var created = await service.CreateCatalogItemAsync(request.ToFields(), cancellationToken);

        // Read back with its brand and type, which the service does not return with a new item.
        var item = (await service.FindCatalogItemAsync(created.Id, cancellationToken))!;
        return TypedResults.Created($"/api/items/{item.Id}", Response(item, links, httpContext));
    }

    // An unknown item is a 404, where the legacy edit answered 500 (audit D11, ADR-0026).
    /// <summary>Replaces an item's fields.</summary>
    /// <remarks>The fields have the rules of a new item. The item keeps its ID and its picture.</remarks>
    /// <param name="id">The item's ID.</param>
    /// <param name="request">The item's new fields.</param>
    /// <response code="204">The item is updated.</response>
    /// <response code="400">The ID is not a 32-bit integer, the body is missing or not an item's JSON, a field breaks its rule, or no brand or type has the ID given.</response>
    /// <response code="404">No item has this ID.</response>
    internal static async Task<Results<NoContent, NotFound, ValidationProblem>> UpdateItemAsync(
        int id, CatalogItemRequest request, ICatalogService service, CancellationToken cancellationToken)
    {
        if (await UnknownBrandOrTypeAsync(request, service, cancellationToken) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await service.UpdateCatalogItemAsync(id, request.ToFields(), cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    // The legacy delete, except for an unknown item, for which the legacy app answered 500 (audit D11).
    /// <summary>Deletes an item.</summary>
    /// <param name="id">The item's ID.</param>
    /// <response code="204">The item is deleted.</response>
    /// <response code="400">The ID is not a 32-bit integer.</response>
    /// <response code="404">No item has this ID.</response>
    internal static async Task<Results<NoContent, NotFound>> DeleteItemAsync(int id, ICatalogService service, CancellationToken cancellationToken) =>
        await service.RemoveCatalogItemAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    // The brand and the type are reference data, which nothing deletes, so checking them first is enough.
    private static async Task<Dictionary<string, string[]>?> UnknownBrandOrTypeAsync(
        CatalogItemRequest request, ICatalogService service, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (await service.FindCatalogBrandAsync(request.CatalogBrandId!.Value, cancellationToken) is null)
        {
            errors[nameof(CatalogItemRequest.CatalogBrandId)] = ["No catalog brand has this ID."];
        }

        if (!(await service.GetCatalogTypesAsync(cancellationToken)).Any(type => type.Id == request.CatalogTypeId))
        {
            errors[nameof(CatalogItemRequest.CatalogTypeId)] = ["No catalog type has this ID."];
        }

        return errors.Count == 0 ? null : errors;
    }

    // The item, with the absolute URL of its picture from the picture route's name, as the legacy controller built it.
    private static CatalogItemResponse Response(CatalogItem item, LinkGenerator links, HttpContext httpContext) =>
        CatalogItemResponse.From(item, links.GetUriByName(httpContext, PictureEndpoints.RouteName, new { catalogItemId = item.Id })!);
}
