namespace eShop.Catalog.Api.Catalog;

// The fields of an item that a caller writes when it creates or updates one (ADR-0015). The ID and the picture are
// not among them: the service assigns the ID, and a new item gets the default picture. The legacy app bound both from
// the posted form (its create then replaced the ID), and its edit wrote every column of the posted object (audit D2).
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
