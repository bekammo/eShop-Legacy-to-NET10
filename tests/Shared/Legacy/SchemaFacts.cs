namespace eShop.Catalog.Api.Tests.Legacy;

// States a schema as one line per column, key, index, foreign key, check constraint or sequence, so
// that schema.json, the EF Core model and a live database can be compared under the same rules
// (docs/legacy/README.md, "Schema"). Column order is not compared, so a table's facts are sorted.
internal static class SchemaFacts
{
    // A default or a computed column is compared by its presence: SQL Server rewrites the expression.
    // The collation appears only when it differs from the database default.
    public static string Column(
        string name, string storeType, bool nullable, (long Seed, long Increment)? identity,
        string? collation, bool hasDefault, bool computed) =>
        $"column {name} {storeType}" +
        (collation is null ? "" : $" collate {collation}") +
        (identity is { } i ? $" identity({i.Seed},{i.Increment})" : "") +
        (nullable ? " null" : " not null") +
        (hasDefault ? " default" : "") +
        (computed ? " computed" : "");

    public static string PrimaryKey(string name, bool clustered, IEnumerable<KeyColumn> columns) =>
        $"primary key {name} {Clustered(clustered)} ({Join(columns)})";

    public static string Index(
        string name, bool unique, bool uniqueConstraint, bool clustered, IEnumerable<KeyColumn> columns,
        IEnumerable<string> included, string? filter)
    {
        var kind = uniqueConstraint ? "unique constraint" : unique ? "unique index" : "index";
        var include = string.Join(", ", included);
        return $"{kind} {name} {Clustered(clustered)} ({Join(columns)})" +
            (include.Length == 0 ? "" : $" include ({include})") +
            (filter is null ? "" : $" where {filter}");
    }

    public static string ForeignKey(
        string name, IEnumerable<string> columns, string principalTable, IEnumerable<string> principalColumns,
        string onDelete, string onUpdate, bool enabled, bool trusted) =>
        $"foreign key {name} ({string.Join(", ", columns)}) references {principalTable} ({string.Join(", ", principalColumns)})" +
        $" on delete {onDelete} on update {onUpdate}{(enabled ? "" : " disabled")}{(trusted ? "" : " not trusted")}";

    public static string CheckConstraint(string name) => $"check constraint {name}";

    // cacheSize is null when the sequence uses the server's default cache size.
    public static string Sequence(string name, string type, long start, long increment, long min, long max, bool cycle, bool cached, int? cacheSize) =>
        $"sequence {name} {type} start {start} increment {increment} min {min} max {max}{(cycle ? " cycle" : " no cycle")}" +
        (!cached ? " no cache" : cacheSize is { } size ? $" cache {size}" : " cache");

    // How many schema objects of one type (sys.objects type_desc) the database has.
    public static string ObjectCount(string type, int count) => $"{count} {type}";

    public static IReadOnlyList<string> Sorted(IEnumerable<string> facts) => [.. facts.Order(StringComparer.Ordinal)];

    private static string Clustered(bool clustered) => clustered ? "clustered" : "nonclustered";

    private static string Join(IEnumerable<KeyColumn> columns) =>
        string.Join(", ", columns.Select(c => c.Descending ? $"{c.Name} desc" : c.Name));
}

// A key or index column, in key order.
internal readonly record struct KeyColumn(string Name, bool Descending);
