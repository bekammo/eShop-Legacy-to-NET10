namespace eShop.Catalog.Api.Catalog;

internal sealed record CatalogItemFields
{
    public required string Name { get; init; }

    public required string? Description { get; init; }

    public required decimal Price { get; init; }

    public required int CatalogTypeId { get; init; }

    public required int CatalogBrandId { get; init; }

    public required int AvailableStock { get; init; }

    public required int RestockThreshold { get; init; }

    public required int MaxStockThreshold { get; init; }

    public required bool OnReorder { get; init; }
}
