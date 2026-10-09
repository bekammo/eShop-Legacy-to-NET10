namespace eShop.Catalog.Api.Tests.Legacy;

internal static class SchemaFacts
{
    // Defaults and computed columns are compared by presence only: SQL Server rewrites their expressions (0 as ((0))).
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

    public static string Sequence(string name, string type, long start, long increment, long min, long max, bool cycle, bool cached, int? cacheSize) =>
        $"sequence {name} {type} start {start} increment {increment} min {min} max {max}{(cycle ? " cycle" : " no cycle")}" +
        (!cached ? " no cache" : cacheSize is { } size ? $" cache {size}" : " cache");

    public static string ObjectCount(string type, int count) => $"{count} {type}";

    public static IReadOnlyList<string> Sorted(IEnumerable<string> facts) => [.. facts.Order(StringComparer.Ordinal)];

    private static string Clustered(bool clustered) => clustered ? "clustered" : "nonclustered";

    private static string Join(IEnumerable<KeyColumn> columns) =>
        string.Join(", ", columns.Select(c => c.Descending ? $"{c.Name} desc" : c.Name));
}

internal readonly record struct KeyColumn(string Name, bool Descending);
