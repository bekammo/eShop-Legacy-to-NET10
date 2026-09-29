using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.UnitTests.Data;

// The schema an EF Core model maps to, as schema facts.
internal static class EfModelSchema
{
    public static IReadOnlyList<string> Tables(IModel model) =>
        SchemaFacts.Sorted(model.GetRelationalModel().Tables.Select(QualifiedName));

    public static IReadOnlyList<string> TableFacts(IModel model, string table)
    {
        var mapped = model.GetRelationalModel().Tables.Single(t => QualifiedName(t) == table);
        var storeObject = StoreObjectIdentifier.Table(mapped.Name, mapped.Schema);
        var facts = new List<string>();

        foreach (var column in mapped.Columns)
        {
            var property = column.PropertyMappings[0].Property;
            (long, long)? identity = property.GetValueGenerationStrategy(storeObject) == SqlServerValueGenerationStrategy.IdentityColumn
                ? (property.GetIdentitySeed(storeObject) ?? 1, property.GetIdentityIncrement(storeObject) ?? 1)
                : null;
            facts.Add(SchemaFacts.Column(
                column.Name,
                column.StoreType,
                column.IsNullable,
                identity,
                column.Collation,
                hasDefault: column.DefaultValue is not null || column.DefaultValueSql is not null,
                computed: column.ComputedColumnSql is not null));
        }

        // Keys have no sort order in EF Core, so every key column is ascending.
        var primaryKey = mapped.PrimaryKey!;
        facts.Add(SchemaFacts.PrimaryKey(
            primaryKey.Name,
            primaryKey.MappedKeys.First().IsClustered(storeObject) ?? true,
            primaryKey.Columns.Select(c => new KeyColumn(c.Name, Descending: false))));

        foreach (var index in mapped.Indexes)
        {
            var mappedIndex = index.MappedIndexes.First();
            facts.Add(SchemaFacts.Index(
                index.Name,
                index.IsUnique,
                uniqueConstraint: false,
                mappedIndex.IsClustered(storeObject) ?? false,
                index.Columns.Select((c, i) => new KeyColumn(c.Name, index.IsDescending?[i] ?? false)),
                (mappedIndex.GetIncludeProperties(storeObject) ?? [])
                    .Select(name => mappedIndex.DeclaringEntityType.FindProperty(name)!.GetColumnName(storeObject)!),
                index.Filter));
        }

        foreach (var uniqueConstraint in mapped.UniqueConstraints.Where(u => u != primaryKey))
        {
            facts.Add(SchemaFacts.Index(
                uniqueConstraint.Name,
                unique: true,
                uniqueConstraint: true,
                uniqueConstraint.MappedKeys.First().IsClustered(storeObject) ?? false,
                uniqueConstraint.Columns.Select(c => new KeyColumn(c.Name, Descending: false)),
                included: [],
                filter: null));
        }

        // EF Core has no update action and always creates enabled, trusted foreign keys.
        foreach (var foreignKey in mapped.ForeignKeyConstraints)
        {
            facts.Add(SchemaFacts.ForeignKey(
                foreignKey.Name,
                foreignKey.Columns.Select(c => c.Name),
                QualifiedName(foreignKey.PrincipalTable),
                foreignKey.PrincipalColumns.Select(c => c.Name),
                OnDelete(foreignKey.OnDeleteAction),
                onUpdate: "NO ACTION",
                enabled: true,
                trusted: true));
        }

        facts.AddRange(mapped.CheckConstraints.Select(check => SchemaFacts.CheckConstraint(check.Name!)));

        return SchemaFacts.Sorted(facts);
    }

    // EF Core has no cache setting for sequences, so it creates them with the server's default cache.
    public static IReadOnlyList<string> SequenceFacts(IModel model) =>
        SchemaFacts.Sorted(model.GetSequences().Select(sequence =>
        {
            var (type, min, max) = StoreType(sequence.Type);
            return SchemaFacts.Sequence(
                $"{sequence.Schema}.{sequence.Name}", type, sequence.StartValue, sequence.IncrementBy,
                sequence.MinValue ?? min, sequence.MaxValue ?? max, sequence.IsCyclic, cached: true, cacheSize: null);
        }));

    private static string QualifiedName(ITable table) => $"{table.Schema}.{table.Name}";

    // sys.foreign_keys reports NO_ACTION, SET_NULL and so on. schema.json, written by
    // docs/legacy/capture/capture.cs, replaces the underscore with a space, and so do the facts.
    private static string OnDelete(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.SetDefault => "SET DEFAULT",
        ReferentialAction.NoAction or ReferentialAction.Restrict => "NO ACTION",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    // A sequence without explicit bounds gets its type's range.
    private static (string Type, long Min, long Max) StoreType(Type type) =>
        type == typeof(long) ? ("bigint", long.MinValue, long.MaxValue)
        : type == typeof(int) ? ("int", int.MinValue, int.MaxValue)
        : type == typeof(short) ? ("smallint", short.MinValue, short.MaxValue)
        : type == typeof(byte) ? ("tinyint", byte.MinValue, byte.MaxValue)
        : throw new ArgumentOutOfRangeException(nameof(type), type, null);
}
