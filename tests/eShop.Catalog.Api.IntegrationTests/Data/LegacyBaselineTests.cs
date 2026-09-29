using System.Globalization;
using System.Security.Cryptography;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// docs/legacy/baseline.sql, which adopts a database that the legacy app created (ADR-0012). Each
// test builds its own legacy database, because the baseline and the tests change it.
public sealed class LegacyBaselineTests(SqlServerFixture sqlServer)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // What the baseline keeps in an adopted database, beyond what a migrated database has.
    private static readonly IReadOnlyList<string> LegacyExtras =
    [
        .. LegacySchema.TableFacts("dbo.__MigrationHistory").Select(fact => $"dbo.__MigrationHistory: {fact}"),
        LegacySchema.SequenceFact("dbo.catalog_brand_hilo"),
        LegacySchema.SequenceFact("dbo.catalog_type_hilo"),
    ];

    [Fact]
    public async Task Adopted_database_matches_a_migrated_one_apart_from_the_legacy_objects()
    {
        var adopted = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        var migrated = await CatalogDatabase.CreateMigratedAsync(sqlServer, CancellationToken);
        await using var connection = await LegacyDatabase.OpenAsync(adopted, CancellationToken);
        var ef6History = await Ef6HistoryAsync(connection);

        await LegacyDatabase.BaselineAsync(connection, CancellationToken);
        await using var context = CatalogDatabase.CreateContext(adopted);
        await context.Database.MigrateAsync(CancellationToken);

        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        await using var migratedConnection = await LegacyDatabase.OpenAsync(migrated, CancellationToken);
        Assert.Equal(
            SchemaFacts.Sorted([.. await SqlServerSchema.SnapshotAsync(migratedConnection, CancellationToken), .. LegacyExtras]),
            await SqlServerSchema.SnapshotAsync(connection, CancellationToken));
        Assert.Equal(LegacySchema.AdoptedObjectCountFacts(), await SqlServerSchema.ObjectCountFactsAsync(connection, CancellationToken));
        Assert.Equal(ef6History, await Ef6HistoryAsync(connection));
    }

    // Deployments apply the migrations with the idempotent script (ADR-0011).
    [Fact]
    public async Task Adopted_database_takes_the_idempotent_migrations_script()
    {
        var adopted = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using var connection = await LegacyDatabase.OpenAsync(adopted, CancellationToken);
        await LegacyDatabase.BaselineAsync(connection, CancellationToken);
        var schema = await SqlServerSchema.SnapshotAsync(connection, CancellationToken);
        await using var context = CatalogDatabase.CreateContext(adopted);

        await SqlScripts.RunAsync(
            connection, context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent), CancellationToken);

        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        Assert.Equal(schema, await SqlServerSchema.SnapshotAsync(connection, CancellationToken));
    }

    [Fact]
    public async Task EF_Core_reads_and_writes_an_adopted_database_with_ids_after_the_legacy_ones()
    {
        var adopted = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using var connection = await LegacyDatabase.OpenAsync(adopted, CancellationToken);
        await LegacyDatabase.BaselineAsync(connection, CancellationToken);
        await using var context = CatalogDatabase.CreateContext(adopted);

        var items = await context.CatalogItems.AsNoTracking()
            .Include(item => item.CatalogBrand).Include(item => item.CatalogType)
            .OrderBy(item => item.Id).ToListAsync(CancellationToken);

        Assert.Equal(LegacySeedData.Items, items.Select(Fact));
        Assert.All(items, item => Assert.Equal((item.CatalogBrandId, item.CatalogTypeId), (item.CatalogBrand!.Id, item.CatalogType!.Id)));

        // The legacy seeding left catalog_hilo at 11, so EF Core's first block starts at 21.
        var added = new CatalogItem { Name = "Adopted", PictureFileName = "dummy.png", Price = 1.50m, CatalogBrandId = 1, CatalogTypeId = 1 };
        context.CatalogItems.Add(added);
        await context.SaveChangesAsync(CancellationToken);
        Assert.Equal(21, added.Id);
        Assert.Equal(21L, await LastUsedItemIdAsync(connection));

        // The legacy app, still running against the database, would take the next block.
        Assert.Equal(31L, await LegacyDatabase.ScalarAsync<long>(connection, "SELECT NEXT VALUE FOR dbo.catalog_hilo", CancellationToken));

        added.Price = 2.50m;
        await context.SaveChangesAsync(CancellationToken);
        Assert.Equal(2.50m, await LegacyDatabase.ScalarAsync<decimal>(connection, "SELECT Price FROM dbo.Catalog WHERE Id = 21", CancellationToken));

        context.CatalogItems.Remove(added);
        await context.SaveChangesAsync(CancellationToken);
        Assert.Equal(12, await LegacyDatabase.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.Catalog", CancellationToken));
    }

    [Fact]
    public async Task Baseline_changes_nothing_when_it_runs_again()
    {
        var adopted = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using var connection = await LegacyDatabase.OpenAsync(adopted, CancellationToken);
        await LegacyDatabase.BaselineAsync(connection, CancellationToken);
        var state = await StateAsync(adopted);

        await LegacyDatabase.BaselineAsync(connection, CancellationToken);

        Assert.Equal(state, await StateAsync(adopted));
    }

    [Fact]
    public async Task Baseline_changes_nothing_in_a_database_that_the_migrations_created()
    {
        var migrated = await CatalogDatabase.CreateMigratedAsync(sqlServer, CancellationToken);
        var state = await StateAsync(migrated);
        await using var connection = await LegacyDatabase.OpenAsync(migrated, CancellationToken);

        await LegacyDatabase.BaselineAsync(connection, CancellationToken);

        Assert.Equal(state, await StateAsync(migrated));
    }

    // A migration run against a legacy database fails on the existing legacy objects, the catalog_hilo
    // sequence first, but EF Core has created its history table by then, outside the migration's transaction.
    [Fact]
    public async Task Baseline_adopts_a_database_that_a_failed_migration_left_behind()
    {
        var adopted = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using var context = CatalogDatabase.CreateContext(adopted);
        await Assert.ThrowsAsync<SqlException>(() => context.Database.MigrateAsync(CancellationToken));
        await using var connection = await LegacyDatabase.OpenAsync(adopted, CancellationToken);
        Assert.Equal(0, await LegacyDatabase.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory", CancellationToken));

        await LegacyDatabase.BaselineAsync(connection, CancellationToken);

        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(CancellationToken));
    }

    public static TheoryData<string> Refusals => [.. Changes.Keys];

    // Changes to a legacy database that the baseline must refuse, with the error number it reports.
    // Each check has cases for both directions (missing and unexpected) where it compares lists.
    private static readonly Dictionary<string, (string Sql, int Error)> Changes = new()
    {
        ["history table of another shape"] = ("CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY)", 50004),
        ["history table with an extra column"] = (
            "CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL, ProductVersion nvarchar(32) NOT NULL, " +
            "Extra int NULL, CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY (MigrationId))", 50004),
        ["history with another migration"] = (
            "CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, " +
            "ProductVersion nvarchar(32) NOT NULL); INSERT INTO dbo.__EFMigrationsHistory VALUES (N'20250101000000_Other', N'10.0.12')", 50005),
        ["column type changed"] = ("ALTER TABLE dbo.Catalog ALTER COLUMN Name nvarchar(60) NOT NULL", 50007),
        ["column collation changed"] = ("ALTER TABLE dbo.Catalog ALTER COLUMN Name nvarchar(50) COLLATE Latin1_General_BIN2 NOT NULL", 50007),
        ["column renamed"] = ("EXEC sp_rename 'dbo.CatalogBrand.Brand', 'BrandName', 'COLUMN'", 50007),
        ["column added"] = ("ALTER TABLE dbo.Catalog ADD Sku nvarchar(32) NULL", 50007),
        ["column dropped"] = ("ALTER TABLE dbo.Catalog DROP COLUMN Description", 50007),
        ["extra index"] = ("CREATE INDEX IX_Catalog_Name ON dbo.Catalog (Name)", 50008),
        ["index dropped"] = ("DROP INDEX IX_CatalogTypeId ON dbo.Catalog", 50008),
        ["index redefined under its legacy name"] = (
            "CREATE INDEX IX_CatalogBrandId ON dbo.Catalog (CatalogBrandId) INCLUDE (Name) WITH (DROP_EXISTING = ON)", 50008),
        ["user statistics"] = ("CREATE STATISTICS ST_Catalog_Name ON dbo.Catalog (Name)", 50008),
        ["trigger"] = ("CREATE TRIGGER dbo.CatalogAudit ON dbo.Catalog AFTER UPDATE AS SET NOCOUNT ON;", 50008),
        ["check constraint"] = ("ALTER TABLE dbo.Catalog ADD CONSTRAINT CK_Catalog_Price CHECK (Price >= 0)", 50008),
        ["foreign key not trusted"] = ("ALTER TABLE dbo.Catalog NOCHECK CONSTRAINT [FK_dbo.Catalog_dbo.CatalogType_CatalogTypeId]", 50008),
        ["foreign key with another update action"] = (
            "ALTER TABLE dbo.Catalog DROP CONSTRAINT [FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId]; " +
            "ALTER TABLE dbo.Catalog ADD CONSTRAINT [FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId] FOREIGN KEY (CatalogBrandId) " +
            "REFERENCES dbo.CatalogBrand (Id) ON DELETE CASCADE ON UPDATE CASCADE", 50008),
        ["foreign key from another table"] = ("CREATE TABLE dbo.Review (Id int PRIMARY KEY, CatalogItemId int NOT NULL REFERENCES dbo.Catalog (Id))", 50008),
        ["legacy-named foreign key in another schema"] = (
            "CREATE SCHEMA archive CREATE TABLE Catalog (Id int NOT NULL CONSTRAINT PK_archive_Catalog PRIMARY KEY, " +
            "CatalogBrandId int NOT NULL CONSTRAINT [FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId] REFERENCES dbo.CatalogBrand (Id) ON DELETE CASCADE)", 50008),
        ["schema-bound view"] = ("CREATE VIEW dbo.ItemNames WITH SCHEMABINDING AS SELECT Id, Name FROM dbo.Catalog", 50008),
        ["sequence increment 1"] = ("ALTER SEQUENCE dbo.catalog_hilo INCREMENT BY 1", 50009),
        ["sequence cycles"] = ("ALTER SEQUENCE dbo.catalog_hilo CYCLE", 50009),
        // Never drawn since the restart, so the next value is 12 itself, the highest item ID.
        ["sequence restarted at the highest ID"] = ("ALTER SEQUENCE dbo.catalog_hilo RESTART WITH 12", 50010),
        // The next value is 21, the ID of the imported item.
        ["item imported at the sequence's next value"] = (
            "INSERT INTO dbo.Catalog (Id, Name, Price, PictureFileName, CatalogTypeId, CatalogBrandId, AvailableStock, RestockThreshold, " +
            "MaxStockThreshold, OnReorder) VALUES (21, N'Imported', 1, N'dummy.png', 1, 1, 0, 0, 0, 0)", 50010),
        ["sequence at the end of the item-ID range"] = ("ALTER SEQUENCE dbo.catalog_hilo RESTART WITH 2147483640", 50010),
        ["brand renamed"] = ("UPDATE dbo.CatalogBrand SET Brand = N'Azure DevOps' WHERE Id = 1", 50011),
        ["brand in another case"] = ("UPDATE dbo.CatalogBrand SET Brand = N'azure' WHERE Id = 1", 50011),
        ["brand with a trailing space"] = ("UPDATE dbo.CatalogBrand SET Brand = N'.NET ' WHERE Id = 2", 50011),
        ["extra brand"] = ("INSERT INTO dbo.CatalogBrand (Brand) VALUES (N'CatalogBrandTestOne')", 50011),
        ["type deleted"] = ("DELETE FROM dbo.CatalogType WHERE Id = 4", 50011),
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Baseline_refuses_a_legacy_database_that_has_changed(string change)
    {
        var (sql, error) = Changes[change];
        var database = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using (var setup = await LegacyDatabase.OpenAsync(database, CancellationToken))
        {
            await LegacyDatabase.ExecuteAsync(setup, sql, CancellationToken);
        }

        await AssertRefusedAsync(database, error);
    }

    [Fact]
    public async Task Baseline_refuses_a_database_that_is_not_a_legacy_one()
    {
        var empty = await LegacyDatabase.CreateEmptyAsync(sqlServer, CancellationToken);

        await AssertRefusedAsync(empty, 50006);
    }

    // A refusal leaves the caller's own transaction as it was: still open, for the caller to end.
    [Fact]
    public async Task Baseline_refuses_to_run_inside_a_transaction()
    {
        var database = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        var state = await StateAsync(database);
        await using var connection = await LegacyDatabase.OpenAsync(database, CancellationToken);
        await LegacyDatabase.ExecuteAsync(connection, "BEGIN TRANSACTION;", CancellationToken);

        var refusal = await Assert.ThrowsAsync<SqlException>(() => LegacyDatabase.BaselineAsync(connection, CancellationToken));

        Assert.Equal(50001, refusal.Number);
        Assert.Equal(1, await LegacyDatabase.ScalarAsync<int>(connection, "SELECT @@TRANCOUNT", CancellationToken));
        await LegacyDatabase.ExecuteAsync(connection, "ROLLBACK;", CancellationToken);
        Assert.Equal(state, await StateAsync(database));
    }

    // With implicit transactions, BEGIN TRANSACTION would nest, and the final COMMIT would leave the
    // adoption uncommitted although the script reports success.
    [Fact]
    public async Task Baseline_refuses_to_run_with_implicit_transactions()
    {
        var database = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using var connection = await LegacyDatabase.OpenAsync(database, CancellationToken, pooling: false);
        await LegacyDatabase.ExecuteAsync(connection, "SET IMPLICIT_TRANSACTIONS ON;", CancellationToken);

        await AssertRefusedAsync(database, 50001, connection);
    }

    // Without VIEW DEFINITION, the catalog views hide schema-bound objects from the login, so the
    // baseline would adopt a database with one.
    [Fact]
    public async Task Baseline_refuses_a_login_without_VIEW_DEFINITION()
    {
        var database = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using var connection = await LegacyDatabase.OpenAsync(database, CancellationToken, pooling: false);
        await ImpersonateMigratorAsync(connection, viewDefinition: false);

        await AssertRefusedAsync(database, 50012, connection);
    }

    // The rights docs/legacy/README.md documents: db_ddladmin, db_datareader, db_datawriter and VIEW DEFINITION.
    [Fact]
    public async Task Baseline_adopts_with_the_documented_rights_and_still_sees_schema_bound_objects()
    {
        var adopted = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        var withView = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using (var setup = await LegacyDatabase.OpenAsync(withView, CancellationToken))
        {
            await LegacyDatabase.ExecuteAsync(setup, Changes["schema-bound view"].Sql, CancellationToken);
        }

        await using (var connection = await LegacyDatabase.OpenAsync(adopted, CancellationToken, pooling: false))
        {
            await ImpersonateMigratorAsync(connection, viewDefinition: true);
            await LegacyDatabase.BaselineAsync(connection, CancellationToken);
        }

        await using var context = CatalogDatabase.CreateContext(adopted);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        await using var viewConnection = await LegacyDatabase.OpenAsync(withView, CancellationToken, pooling: false);
        await ImpersonateMigratorAsync(viewConnection, viewDefinition: true);
        await AssertRefusedAsync(withView, 50008, viewConnection);
    }

    // The documented procedure runs the script with sqlcmd -b, and the exit code tells success from refusal.
    [Fact]
    public async Task Sqlcmd_exits_with_0_when_the_baseline_adopts_a_database_and_with_1_when_it_refuses()
    {
        var script = LegacyFiles.ReadText("baseline.sql");
        var legacy = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        var empty = await LegacyDatabase.CreateEmptyAsync(sqlServer, CancellationToken);

        var adopted = await sqlServer.RunSqlcmdAsync(DatabaseName(legacy), script, CancellationToken);
        var again = await sqlServer.RunSqlcmdAsync(DatabaseName(legacy), script, CancellationToken);
        var refused = await sqlServer.RunSqlcmdAsync(DatabaseName(empty), script, CancellationToken);

        Assert.True(adopted.ExitCode == 0, adopted.Stdout + adopted.Stderr);
        Assert.Contains("Baselined", adopted.Stdout, StringComparison.Ordinal);
        Assert.True(again.ExitCode == 0, again.Stdout + again.Stderr);
        Assert.Contains("nothing to do", again.Stdout, StringComparison.Ordinal);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("Msg 50006", refused.Stdout + refused.Stderr, StringComparison.Ordinal);
    }

    // A refusal reports its number, leaves no transaction open, and changes nothing.
    // Runs the baseline on the given connection, or on a new one, and checks the refusal.
    private static async Task AssertRefusedAsync(string database, int error, SqlConnection? connection = null)
    {
        var state = await StateAsync(database);
        await using var ownConnection = connection is null ? await LegacyDatabase.OpenAsync(database, CancellationToken) : null;
        var baselineConnection = connection ?? ownConnection!;

        var refusal = await Assert.ThrowsAsync<SqlException>(() => LegacyDatabase.BaselineAsync(baselineConnection, CancellationToken));

        Assert.True(refusal.Number == error, $"Expected error {error}, got {refusal.Number}: {refusal.Message}");
        Assert.Equal(0, await LegacyDatabase.ScalarAsync<int>(baselineConnection, "SELECT @@TRANCOUNT", CancellationToken));
        Assert.Equal(state, await StateAsync(database));
    }

    // Makes the connection run as a database user with exactly the documented roles. Such connections
    // are not pooled (LegacyDatabase.OpenAsync with pooling off), so the impersonation ends with them.
    private static async Task ImpersonateMigratorAsync(SqlConnection connection, bool viewDefinition)
    {
        await LegacyDatabase.ExecuteAsync(
            connection,
            "CREATE USER migrator WITHOUT LOGIN; ALTER ROLE db_ddladmin ADD MEMBER migrator; " +
            "ALTER ROLE db_datareader ADD MEMBER migrator; ALTER ROLE db_datawriter ADD MEMBER migrator;" +
            (viewDefinition ? " GRANT VIEW DEFINITION TO migrator;" : ""),
            CancellationToken);
        await LegacyDatabase.ExecuteAsync(connection, "EXECUTE AS USER = N'migrator';", CancellationToken);
    }

    // Everything the baseline could change, read on a connection of its own: the schema, the object
    // counts, the rows of both history tables and of the catalog tables, the sequences' positions and
    // the identity values.
    private static async Task<IReadOnlyList<string>> StateAsync(string database)
    {
        await using var connection = await LegacyDatabase.OpenAsync(database, CancellationToken);
        return
        [
            .. await SqlServerSchema.SnapshotAsync(connection, CancellationToken),
            .. await SqlServerSchema.ObjectCountFactsAsync(connection, CancellationToken),
            .. (await RowsAsync(connection, "__EFMigrationsHistory")).Select(row => $"EF Core history: {row}"),
            .. await Ef6HistoryAsync(connection),
            .. (await RowsAsync(connection, "Catalog")).Select(row => $"item: {row}"),
            .. (await RowsAsync(connection, "CatalogBrand")).Select(row => $"brand: {row}"),
            .. (await RowsAsync(connection, "CatalogType")).Select(row => $"type: {row}"),
            .. await ColumnAsync(
                connection,
                "SELECT CONCAT(N'sequence ', name, N' current ', CAST(current_value AS nvarchar(40)), N' last used ', " +
                "ISNULL(CAST(last_used_value AS nvarchar(40)), N'none')) FROM sys.sequences"),
            .. await ColumnAsync(
                connection,
                "SELECT CONCAT(N'identity CatalogBrand ', IDENT_CURRENT(N'dbo.CatalogBrand'), N' CatalogType ', IDENT_CURRENT(N'dbo.CatalogType'))"),
        ];
    }

    private static async Task<IReadOnlyList<string>> ColumnAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return SchemaFacts.Sorted(values);
    }

    private static Task<IReadOnlyList<string>> Ef6HistoryAsync(SqlConnection connection) => RowsAsync(connection, "__MigrationHistory");

    // Every column of every row of a dbo table as text, sorted; binary values as their SHA-256.
    // None when the table does not exist.
    private static async Task<IReadOnlyList<string>> RowsAsync(SqlConnection connection, string table)
    {
        if (await LegacyDatabase.ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM sys.tables WHERE name = N'{table}'", CancellationToken) == 0)
        {
            return [];
        }

        await using var command = new SqlCommand($"SELECT * FROM dbo.{table}", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(string.Join(" ", values.Select(value => value is byte[] bytes
                ? Convert.ToHexString(SHA256.HashData(bytes))
                : Convert.ToString(value, CultureInfo.InvariantCulture))));
        }

        return SchemaFacts.Sorted(rows);
    }

    private static Task<long> LastUsedItemIdAsync(SqlConnection connection) =>
        LegacyDatabase.ScalarAsync<long>(
            connection, "SELECT CAST(last_used_value AS bigint) FROM sys.sequences WHERE name = N'catalog_hilo'", CancellationToken);

    private static string DatabaseName(string connectionString) => new SqlConnectionStringBuilder(connectionString).InitialCatalog;

    private static string Fact(CatalogItem item) => LegacySeedData.Item(
        item.Id, item.Name, item.Description, item.Price, item.PictureFileName, item.CatalogTypeId, item.CatalogBrandId,
        item.AvailableStock, item.RestockThreshold, item.MaxStockThreshold, item.OnReorder);
}
