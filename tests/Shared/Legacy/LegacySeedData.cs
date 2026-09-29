using System.Globalization;
using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.Tests.Legacy;

// The rows the legacy app seeds into a fresh database, from docs/legacy/seed-data.json (Stage 1.2).
internal static class LegacySeedData
{
    private static readonly JsonNode SeedData = LegacyFiles.ReadJson("seed-data.json");

    // One "<Id> <name>" line per brand, in ID order.
    public static IReadOnlyList<string> Brands => Rows("CatalogBrand", "Brand");

    // One "<Id> <name>" line per type, in ID order.
    public static IReadOnlyList<string> Types => Rows("CatalogType", "Type");

    public static string Row(int id, string name) => $"{id.ToString(CultureInfo.InvariantCulture)} {name}";

    private static IReadOnlyList<string> Rows(string table, string nameColumn) =>
        [.. SeedData[table]!.AsArray().Select(row => Row((int)row!["Id"]!, (string)row[nameColumn]!))];
}
