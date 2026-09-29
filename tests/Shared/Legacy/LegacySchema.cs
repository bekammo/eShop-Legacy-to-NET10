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

    // The object counts a database migrated by the new API must have: the legacy counts, less the
    // objects the comparison leaves out, plus EF Core's history table. This catches objects that
    // the other facts do not describe, such as triggers, views or procedures.
    public static IReadOnlyList<string> ObjectCountFacts()
    {
        var counts = Schema["objectCounts"]!.AsObject().ToDictionary(p => p.Key, p => (int)p.Value!);

        // The unused catalog_brand_hilo and catalog_type_hilo.
        counts["SEQUENCE_OBJECT"] -= 2;

        // EF6's __MigrationHistory and its primary key give way to EF Core's __EFMigrationsHistory
        // and its primary key, so USER_TABLE and PRIMARY_KEY_CONSTRAINT stay as they are.
        return SchemaFacts.Sorted(counts.Where(c => c.Value > 0).Select(c => SchemaFacts.ObjectCount(c.Key, c.Value)));
    }

    // The object counts of a legacy database after the Stage 4.3 baseline: every legacy object
    // stays, and EF Core's history table and its primary key are added.
    public static IReadOnlyList<string> AdoptedObjectCountFacts()
    {
        var counts = Schema["objectCounts"]!.AsObject().ToDictionary(p => p.Key, p => (int)p.Value!);
        counts["USER_TABLE"] += 1;
        counts["PRIMARY_KEY_CONSTRAINT"] += 1;
        return SchemaFacts.Sorted(counts.Select(c => SchemaFacts.ObjectCount(c.Key, c.Value)));
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
