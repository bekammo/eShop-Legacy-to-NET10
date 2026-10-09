using System.Globalization;
using System.Text.Json.Nodes;
using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Tests.Legacy;

internal static class LegacySeedData
{
    private static readonly JsonNode SeedData = LegacyFiles.ReadJson("seed-data.json");

    public static IReadOnlyList<string> Brands => [.. Table("CatalogBrand").Select(row => Row((int)row["Id"]!, (string)row["Brand"]!))];

    public static IReadOnlyList<string> Types => [.. Table("CatalogType").Select(row => Row((int)row["Id"]!, (string)row["Type"]!))];

    public static IReadOnlyList<string> Items => [.. Table("Catalog").Select(row => Item(
        (int)row["Id"]!, (string)row["Name"]!, (string?)row["Description"], (decimal)row["Price"]!, (string)row["PictureFileName"]!,
        (int)row["CatalogTypeId"]!, (int)row["CatalogBrandId"]!, (int)row["AvailableStock"]!, (int)row["RestockThreshold"]!,
        (int)row["MaxStockThreshold"]!, (bool)row["OnReorder"]!))];

    public static IReadOnlyList<string> ItemsWithBrandAndType
    {
        get
        {
            var brands = Table("CatalogBrand").ToDictionary(row => (int)row["Id"]!, row => (string)row["Brand"]!);
            var types = Table("CatalogType").ToDictionary(row => (int)row["Id"]!, row => (string)row["Type"]!);
            return
            [
                .. Table("Catalog").Select(row =>
                    ItemWithBrandAndType((string)row["Name"]!, brands[(int)row["CatalogBrandId"]!], types[(int)row["CatalogTypeId"]!])),
            ];
        }
    }

    public static IReadOnlyDictionary<string, long> SequenceCurrentValues =>
        SeedData["sequenceCurrentValues"]!.AsObject().ToDictionary(p => p.Key, p => (long)p.Value!);

    public static IReadOnlyList<JsonObject> Table(string table) => [.. SeedData[table]!.AsArray().Select(row => row!.AsObject())];

    public static string Row(int id, string name) => $"{id.ToString(CultureInfo.InvariantCulture)} {name}";

    public static string Item(CatalogItem item) => Item(
        item.Id, item.Name, item.Description, item.Price, item.PictureFileName, item.CatalogTypeId, item.CatalogBrandId,
        item.AvailableStock, item.RestockThreshold, item.MaxStockThreshold, item.OnReorder);

    public static string ItemWithBrandAndType(CatalogItem item) =>
        ItemWithBrandAndType(item.Name, item.CatalogBrand?.Brand ?? "(not loaded)", item.CatalogType?.Type ?? "(not loaded)");

    public static string ItemWithBrandAndType(string name, string brand, string type) => $"{name} | {brand} | {type}";

    public static string Item(
        int id, string name, string? description, decimal price, string pictureFileName, int catalogTypeId, int catalogBrandId,
        int availableStock, int restockThreshold, int maxStockThreshold, bool onReorder) =>
        string.Join(" | ", [
            Row(id, name),
            description ?? "(null)",
            price.ToString(CultureInfo.InvariantCulture),
            pictureFileName,
            $"type {catalogTypeId.ToString(CultureInfo.InvariantCulture)}",
            $"brand {catalogBrandId.ToString(CultureInfo.InvariantCulture)}",
            $"stock {availableStock.ToString(CultureInfo.InvariantCulture)}/{restockThreshold.ToString(CultureInfo.InvariantCulture)}/{maxStockThreshold.ToString(CultureInfo.InvariantCulture)}",
            onReorder ? "on reorder" : "not on reorder",
        ]);
}
