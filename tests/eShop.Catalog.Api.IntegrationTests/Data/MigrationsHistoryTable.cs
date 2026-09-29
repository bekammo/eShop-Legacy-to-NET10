using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// EF Core's history table as EF Core 10 creates it, in dbo (ADR-0010), as schema facts. The Stage 4.3
// baseline creates the same table in a legacy database, so both are checked against this.
internal static class MigrationsHistoryTable
{
    public const string Name = "dbo.__EFMigrationsHistory";

    public static readonly IReadOnlyList<string> Facts = SchemaFacts.Sorted(
    [
        SchemaFacts.Column("MigrationId", "nvarchar(150)", nullable: false, identity: null, collation: null, hasDefault: false, computed: false),
        SchemaFacts.Column("ProductVersion", "nvarchar(32)", nullable: false, identity: null, collation: null, hasDefault: false, computed: false),
        SchemaFacts.PrimaryKey("PK___EFMigrationsHistory", clustered: true, [new KeyColumn("MigrationId", Descending: false)]),
    ]);
}
