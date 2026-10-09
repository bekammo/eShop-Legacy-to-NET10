using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.Tests.Legacy;

internal static class LegacySchema
{
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
                (string)foreignKey["onDelete"]!,
                (string)foreignKey["onUpdate"]!,
                (bool)foreignKey["enabled"]!,
                (bool)foreignKey["trusted"]!));
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
            (bool)json["cycle"]!,
            (bool)json["cached"]!,
            (int?)json["cacheSize"]);
    }

    public static IReadOnlyList<string> ObjectCountFacts()
    {
        var counts = Schema["objectCounts"]!.AsObject().ToDictionary(p => p.Key, p => (int)p.Value!);

        counts["SEQUENCE_OBJECT"] -= 2;

        // USER_TABLE and PRIMARY_KEY_CONSTRAINT stay unchanged: EF Core's history table and key replace EF6's
        // one for one.
        return SchemaFacts.Sorted(counts.Where(c => c.Value > 0).Select(c => SchemaFacts.ObjectCount(c.Key, c.Value)));
    }

    public static IReadOnlyList<string> AdoptedObjectCountFacts()
    {
        var counts = Schema["objectCounts"]!.AsObject().ToDictionary(p => p.Key, p => (int)p.Value!);
        counts["USER_TABLE"] += 1;
        counts["PRIMARY_KEY_CONSTRAINT"] += 1;
        return SchemaFacts.Sorted(counts.Select(c => SchemaFacts.ObjectCount(c.Key, c.Value)));
    }

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
