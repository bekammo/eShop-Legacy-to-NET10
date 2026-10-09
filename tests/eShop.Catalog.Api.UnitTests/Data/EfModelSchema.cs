using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.UnitTests.Data;

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

    public static IReadOnlyList<string> SequenceFacts(IModel model) =>
        SchemaFacts.Sorted(model.GetSequences().Select(sequence =>
        {
            var (type, min, max) = StoreType(sequence.Type);
            return SchemaFacts.Sequence(
                $"{sequence.Schema}.{sequence.Name}", type, sequence.StartValue, sequence.IncrementBy,
                sequence.MinValue ?? min, sequence.MaxValue ?? max, sequence.IsCyclic, cached: true, cacheSize: null);
        }));

    private static string QualifiedName(ITable table) => $"{table.Schema}.{table.Name}";

    // A space, not sys.foreign_keys' underscore (NO_ACTION): schema.json writes NO ACTION, and the facts must match it.
    private static string OnDelete(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.SetDefault => "SET DEFAULT",
        ReferentialAction.NoAction or ReferentialAction.Restrict => "NO ACTION",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static (string Type, long Min, long Max) StoreType(Type type) =>
        type == typeof(long) ? ("bigint", long.MinValue, long.MaxValue)
        : type == typeof(int) ? ("int", int.MinValue, int.MaxValue)
        : type == typeof(short) ? ("smallint", short.MinValue, short.MaxValue)
        : type == typeof(byte) ? ("tinyint", byte.MinValue, byte.MaxValue)
        : throw new ArgumentOutOfRangeException(nameof(type), type, null);
}
