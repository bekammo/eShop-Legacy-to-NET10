using eShop.Catalog.Api.Tests.Legacy;

namespace eShop.Catalog.Api.IntegrationTests.Data;

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
