using System.Globalization;
using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.Tests.Legacy;

// The rows the legacy app seeds into a fresh database, from docs/legacy/seed-data.json (Stage 1.2).
internal static class LegacySeedData
{
    private static readonly JsonNode SeedData = LegacyFiles.ReadJson("seed-data.json");

    // One "<Id> <name>" line per brand, in ID order.
    public static IReadOnlyList<string> Brands => [.. Table("CatalogBrand").Select(row => Row((int)row["Id"]!, (string)row["Brand"]!))];

    // One "<Id> <name>" line per type, in ID order.
    public static IReadOnlyList<string> Types => [.. Table("CatalogType").Select(row => Row((int)row["Id"]!, (string)row["Type"]!))];

    // One line per item, in ID order, with every column.
    public static IReadOnlyList<string> Items => [.. Table("Catalog").Select(row => Item(
        (int)row["Id"]!, (string)row["Name"]!, (string?)row["Description"], (decimal)row["Price"]!, (string)row["PictureFileName"]!,
        (int)row["CatalogTypeId"]!, (int)row["CatalogBrandId"]!, (int)row["AvailableStock"]!, (int)row["RestockThreshold"]!,
        (int)row["MaxStockThreshold"]!, (bool)row["OnReorder"]!))];

    // The last value the legacy seeding drew from each sequence (sys.sequences.current_value).
    public static IReadOnlyDictionary<string, long> SequenceCurrentValues =>
        SeedData["sequenceCurrentValues"]!.AsObject().ToDictionary(p => p.Key, p => (long)p.Value!);

    // The rows of one table, as seed-data.json records them.
    public static IReadOnlyList<JsonObject> Table(string table) => [.. SeedData[table]!.AsArray().Select(row => row!.AsObject())];

    public static string Row(int id, string name) => $"{id.ToString(CultureInfo.InvariantCulture)} {name}";

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
