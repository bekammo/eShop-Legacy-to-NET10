using System.Globalization;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.Data.SqlClient;

namespace eShop.Catalog.Api.IntegrationTests.Data;

internal static class SqlServerSchema
{
    private const string HistoryTable = "__EFMigrationsHistory";

    public static async Task<IReadOnlyList<string>> TablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            connection,
            "SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name <> @history",
            [new SqlParameter("@history", HistoryTable)],
            cancellationToken);
        return SchemaFacts.Sorted(rows.Select(r => (string)r[0]));
    }

    public static async Task<IReadOnlyList<string>> SnapshotAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = await QueryAsync(
            connection, "SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id", [], cancellationToken);
        var facts = new List<string>();
        foreach (var table in tables.Select(r => (string)r[0]))
        {
            facts.AddRange((await TableFactsAsync(connection, table, cancellationToken)).Select(fact => $"{table}: {fact}"));
        }

        facts.AddRange(await SequenceFactsAsync(connection, cancellationToken));
        facts.AddRange(await ObjectsOutsideSysObjectsAsync(connection, cancellationToken));
        return SchemaFacts.Sorted(facts);
    }

    public static async Task<IReadOnlyList<string>> TableFactsAsync(SqlConnection connection, string table, CancellationToken cancellationToken)
    {
        var databaseCollation = (string)(await QueryAsync(
            connection, "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))", [], cancellationToken))[0][0];
        var facts = new List<string>();

        foreach (var c in await QueryAsync(
            connection,
            "SELECT c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, " +
            "CAST(ic.seed_value AS bigint), CAST(ic.increment_value AS bigint), c.collation_name, " +
            "CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit), c.is_computed " +
            "FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id " +
            "LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id " +
            "WHERE c.object_id = OBJECT_ID(@table)",
            [Table(table)],
            cancellationToken))
        {
            var collation = c[9] as string;
            facts.Add(SchemaFacts.Column(
                (string)c[0],
                StoreType((string)c[1], Convert.ToInt32(c[2], CultureInfo.InvariantCulture), Convert.ToInt32(c[3], CultureInfo.InvariantCulture), Convert.ToInt32(c[4], CultureInfo.InvariantCulture)),
                (bool)c[5],
                (bool)c[6] ? ((long)c[7], (long)c[8]) : null,
                collation == databaseCollation ? null : collation,
                hasDefault: (bool)c[10],
                computed: (bool)c[11]));
        }

        var indexColumns = (await QueryAsync(
            connection,
            "SELECT ic.index_id, c.name, ic.is_descending_key, ic.is_included_column FROM sys.index_columns ic " +
            "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE ic.object_id = OBJECT_ID(@table) ORDER BY ic.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id",
            [Table(table)],
            cancellationToken)).ToLookup(r => (int)r[0]);

        foreach (var i in await QueryAsync(
            connection,
            "SELECT i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.filter_definition " +
            "FROM sys.indexes i WHERE i.object_id = OBJECT_ID(@table) AND i.type > 0",
            [Table(table)],
            cancellationToken))
        {
            var columns = indexColumns[(int)i[0]].ToList();
            var keyColumns = columns.Where(r => !(bool)r[3]).Select(r => new KeyColumn((string)r[1], (bool)r[2]));
            var clustered = (string)i[2] == "CLUSTERED";
            facts.Add((bool)i[4]
                ? SchemaFacts.PrimaryKey((string)i[1], clustered, keyColumns)
                : SchemaFacts.Index(
                    (string)i[1], (bool)i[3], (bool)i[5], clustered, keyColumns,
                    columns.Where(r => (bool)r[3]).Select(r => (string)r[1]),
                    i[6] as string));
        }

        var foreignKeyColumns = (await QueryAsync(
            connection,
            "SELECT fkc.constraint_object_id, pc.name, rc.name FROM sys.foreign_key_columns fkc " +
            "JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id " +
            "JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id " +
            "WHERE fkc.parent_object_id = OBJECT_ID(@table) ORDER BY fkc.constraint_object_id, fkc.constraint_column_id",
            [Table(table)],
            cancellationToken)).ToLookup(r => (int)r[0]);

        foreach (var fk in await QueryAsync(
            connection,
            "SELECT fk.object_id, fk.name, OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id), " +
            "fk.delete_referential_action_desc, fk.update_referential_action_desc, fk.is_disabled, fk.is_not_trusted " +
            "FROM sys.foreign_keys fk WHERE fk.parent_object_id = OBJECT_ID(@table)",
            [Table(table)],
            cancellationToken))
        {
            var columns = foreignKeyColumns[(int)fk[0]].ToList();

            facts.Add(SchemaFacts.ForeignKey(
                (string)fk[1],
                columns.Select(r => (string)r[1]),
                (string)fk[2],
                columns.Select(r => (string)r[2]),
                ((string)fk[3]).Replace('_', ' '),
                ((string)fk[4]).Replace('_', ' '),
                enabled: !(bool)fk[5],
                trusted: !(bool)fk[6]));
        }

        foreach (var check in await QueryAsync(
            connection,
            "SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(@table)",
            [Table(table)],
            cancellationToken))
        {
            facts.Add(SchemaFacts.CheckConstraint((string)check[0]));
        }

        return SchemaFacts.Sorted(facts);
    }

    public static async Task<IReadOnlyList<string>> SequenceFactsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            connection,
            "SELECT s.name + '.' + seq.name, TYPE_NAME(seq.user_type_id), CAST(seq.start_value AS bigint), CAST(seq.increment AS bigint), " +
            "CAST(seq.minimum_value AS bigint), CAST(seq.maximum_value AS bigint), seq.is_cycling, seq.is_cached, seq.cache_size " +
            "FROM sys.sequences seq JOIN sys.schemas s ON s.schema_id = seq.schema_id",
            [],
            cancellationToken);
        return SchemaFacts.Sorted(rows.Select(r => SchemaFacts.Sequence(
            (string)r[0], (string)r[1], (long)r[2], (long)r[3], (long)r[4], (long)r[5], (bool)r[6], (bool)r[7], r[8] as int?)));
    }

    // Must match capture.cs's condition for schema.json's objectCounts, which also counts tables marked is_ms_shipped,
    // as EF6 can mark its history table.
    public static async Task<IReadOnlyList<string>> ObjectCountFactsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            connection,
            "SELECT type_desc, COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0 OR object_id IN (SELECT object_id FROM sys.tables) " +
            "GROUP BY type_desc",
            [],
            cancellationToken);
        return SchemaFacts.Sorted(rows.Select(r => SchemaFacts.ObjectCount((string)r[0], (int)r[1])));
    }

    public static async Task<IReadOnlyList<string>> ObjectsOutsideSysObjectsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            connection,
            "SELECT 'schema ' + name FROM sys.schemas WHERE schema_id BETWEEN 5 AND 16383 " +
            "UNION ALL SELECT 'type ' + SCHEMA_NAME(schema_id) + '.' + name FROM sys.types WHERE is_user_defined = 1 " +
            "UNION ALL SELECT 'database trigger ' + name FROM sys.triggers WHERE parent_class = 0",
            [],
            cancellationToken);
        return SchemaFacts.Sorted(rows.Select(r => (string)r[0]));
    }

    private static string StoreType(string type, int maxLength, int precision, int scale) => type switch
    {
        "nvarchar" or "nchar" => $"{type}({(maxLength == -1 ? "max" : (maxLength / 2).ToString(CultureInfo.InvariantCulture))})",
        "varchar" or "char" or "varbinary" or "binary" => $"{type}({(maxLength == -1 ? "max" : maxLength.ToString(CultureInfo.InvariantCulture))})",
        "decimal" or "numeric" => $"{type}({precision.ToString(CultureInfo.InvariantCulture)},{scale.ToString(CultureInfo.InvariantCulture)})",
        "datetime2" or "time" or "datetimeoffset" => $"{type}({scale.ToString(CultureInfo.InvariantCulture)})",
        _ => type,
    };

    private static SqlParameter Table(string table) => new("@table", table);

    private static async Task<List<object[]>> QueryAsync(
        SqlConnection connection, string sql, SqlParameter[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<object[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }
}
