using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.Tests.Legacy;

// The schema the legacy app creates, from docs/legacy/schema.json (Stage 1.2), as schema facts.
internal static class LegacySchema
{
    // What the new schema must reproduce. __MigrationHistory (EF6's own table) and the unused brand
    // and type sequences are not compared (docs/legacy/README.md, "Schema").
    public static readonly IReadOnlyList<string> Tables = ["dbo.Catalog", "dbo.CatalogBrand", "dbo.CatalogType"];
    public const string ItemIdSequence = "dbo.catalog_hilo";

    private static readonly JsonNode Schema = ReadSchema();

    public static IReadOnlyList<string> TableFacts(string table)
    {
        var json = Schema["tables"]!.AsArray().Single(t => QualifiedName(t!) == table)!;
        var databaseCollation = (string)Schema["collation"]!;
        var facts = new List<string>();

        foreach (var column in json["columns"]!.AsArray())
        {
            var identity = column!["identity"];
            var collation = (string?)column["collation"];
            facts.Add(SchemaFacts.Column(
                (string)column["name"]!,
                (string)column["storeType"]!,
                (bool)column["nullable"]!,
                identity is null ? null : ((long)identity["seed"]!, (long)identity["increment"]!),
                collation == databaseCollation ? null : collation,
                hasDefault: column["default"] is not null,
                computed: (bool)column["computed"]!));
        }

        var primaryKey = json["primaryKey"]!;
        facts.Add(SchemaFacts.PrimaryKey((string)primaryKey["name"]!, (bool)primaryKey["clustered"]!, KeyColumns(primaryKey)));

        foreach (var index in json["indexes"]!.AsArray())
        {
            facts.Add(SchemaFacts.Index(
                (string)index!["name"]!,
                (bool)index["unique"]!,
                (bool)index["uniqueConstraint"]!,
                (bool)index["clustered"]!,
                KeyColumns(index),
                index["included"]!.AsArray().Select(c => (string)c!),
                (string?)index["filter"]));
        }

        foreach (var foreignKey in json["foreignKeys"]!.AsArray())
        {
            facts.Add(SchemaFacts.ForeignKey(
                (string)foreignKey!["name"]!,
                foreignKey["columns"]!.AsArray().Select(c => (string)c!),
                (string)foreignKey["principalTable"]!,
                foreignKey["principalColumns"]!.AsArray().Select(c => (string)c!),
                (string)foreignKey["onDelete"]!));
        }

        return SchemaFacts.Sorted(facts);
    }

    public static string SequenceFact(string sequence)
    {
        var json = Schema["sequences"]!.AsArray().Single(s => QualifiedName(s!) == sequence)!;
        return SchemaFacts.Sequence(
            sequence,
            (string)json["type"]!,
            (long)json["startValue"]!,
            (long)json["increment"]!,
            (long)json["minValue"]!,
            (long)json["maxValue"]!,
            (bool)json["cycle"]!);
    }

    // schema.json lists check constraints only as a count. The legacy schema has none, so the tables
    // have no check-constraint facts; a capture that found some would need them listed per table.
    private static JsonNode ReadSchema()
    {
        var schema = LegacyFiles.ReadJson("schema.json");
        if (schema["objectCounts"]!["CHECK_CONSTRAINT"] is not null)
        {
            throw new NotSupportedException("schema.json counts check constraints but does not list them per table.");
        }

        return schema;
    }

    private static string QualifiedName(JsonNode node) => $"{(string)node["schema"]!}.{(string)node["name"]!}";

    private static IEnumerable<KeyColumn> KeyColumns(JsonNode keyOrIndex) =>
        keyOrIndex["columns"]!.AsArray().Select(c => new KeyColumn((string)c!["name"]!, (bool)c["descending"]!));
}
