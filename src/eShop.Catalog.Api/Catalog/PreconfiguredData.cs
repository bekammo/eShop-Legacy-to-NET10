namespace eShop.Catalog.Api.Catalog;

internal static class PreconfiguredData
{
    public static IReadOnlyList<CatalogBrand> CatalogBrands() =>
    [
        new() { Id = 1, Brand = "Azure" },
        new() { Id = 2, Brand = ".NET" },
        new() { Id = 3, Brand = "Visual Studio" },
        new() { Id = 4, Brand = "SQL Server" },
        new() { Id = 5, Brand = "Other" },
    ];

    public static IReadOnlyList<CatalogType> CatalogTypes() =>
    [
        new() { Id = 1, Type = "Mug" },
        new() { Id = 2, Type = "T-Shirt" },
        new() { Id = 3, Type = "Sheet" },
        new() { Id = 4, Type = "USB Memory Stick" },
    ];

    public static IReadOnlyList<CatalogItem> CatalogItems() =>
    [
        Item(".NET Bot Black Hoodie", 19.50m, "1.png", catalogTypeId: 2, catalogBrandId: 2),
        Item(".NET Black & White Mug", 8.50m, "2.png", catalogTypeId: 1, catalogBrandId: 2),
        Item("Prism White T-Shirt", 12.00m, "3.png", catalogTypeId: 2, catalogBrandId: 5),
        Item(".NET Foundation T-shirt", 12.00m, "4.png", catalogTypeId: 2, catalogBrandId: 2),
        Item("Roslyn Red Sheet", 8.50m, "5.png", catalogTypeId: 3, catalogBrandId: 5),
        Item(".NET Blue Hoodie", 12.00m, "6.png", catalogTypeId: 2, catalogBrandId: 2),
        Item("Roslyn Red T-Shirt", 12.00m, "7.png", catalogTypeId: 2, catalogBrandId: 5),
        Item("Kudu Purple Hoodie", 8.50m, "8.png", catalogTypeId: 2, catalogBrandId: 5),
        Item("Cup<T> White Mug", 12.00m, "9.png", catalogTypeId: 1, catalogBrandId: 5),
        Item(".NET Foundation Sheet", 12.00m, "10.png", catalogTypeId: 3, catalogBrandId: 2),
        Item("Cup<T> Sheet", 8.50m, "11.png", catalogTypeId: 3, catalogBrandId: 2),
        Item("Prism White TShirt", 12.00m, "12.png", catalogTypeId: 2, catalogBrandId: 5),
    ];

    private static CatalogItem Item(string name, decimal price, string pictureFileName, int catalogTypeId, int catalogBrandId) => new()
    {
        Name = name,
        Description = name,
        Price = price,
        PictureFileName = pictureFileName,
        CatalogTypeId = catalogTypeId,
        CatalogBrandId = catalogBrandId,
        AvailableStock = 100,
    };
}
