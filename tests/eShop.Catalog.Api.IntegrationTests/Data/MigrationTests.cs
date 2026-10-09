using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Data;

[Trait("Category", "Docker")]
public sealed class MigrationTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    public static TheoryData<string> LegacyTables => [.. LegacySchema.Tables];

    [Fact]
    public async Task Every_migration_is_applied()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database;

        var applied = await database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(applied);
        Assert.Equal(database.GetMigrations(), applied);
    }

    [Fact]
    public async Task Database_has_exactly_the_legacy_catalog_tables()
    {
        await using var connection = await OpenAsync();

        Assert.Equal(LegacySchema.Tables, await SqlServerSchema.TablesAsync(connection, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(LegacyTables))]
    public async Task Table_matches_the_legacy_schema(string table)
    {
        await using var connection = await OpenAsync();

        Assert.Equal(
            LegacySchema.TableFacts(table),
            await SqlServerSchema.TableFactsAsync(connection, table, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Only_sequence_is_the_legacy_item_id_sequence()
    {
        await using var connection = await OpenAsync();

        Assert.Equal(
            [LegacySchema.SequenceFact(LegacySchema.ItemIdSequence)],
            await SqlServerSchema.SequenceFactsAsync(connection, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Database_has_no_objects_beyond_the_legacy_schema()
    {
        await using var connection = await OpenAsync();

        Assert.Equal(LegacySchema.ObjectCountFacts(), await SqlServerSchema.ObjectCountFactsAsync(connection, TestContext.Current.CancellationToken));
        Assert.Empty(await SqlServerSchema.ObjectsOutsideSysObjectsAsync(connection, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrations_history_table_is_in_dbo_as_ef_core_creates_it()
    {
        await using var connection = await OpenAsync();

        Assert.Equal(
            MigrationsHistoryTable.Facts,
            await SqlServerSchema.TableFactsAsync(connection, MigrationsHistoryTable.Name, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Brands_have_their_legacy_ids()
    {
        await using var connection = await OpenAsync();

        Assert.Equal(LegacySeedData.Brands, await ColumnAsync(connection, "SELECT CONCAT(Id, ' ', Brand) FROM dbo.CatalogBrand ORDER BY Id"));
    }

    [Fact]
    public async Task Types_have_their_legacy_ids()
    {
        await using var connection = await OpenAsync();

        Assert.Equal(LegacySeedData.Types, await ColumnAsync(connection, "SELECT CONCAT(Id, ' ', Type) FROM dbo.CatalogType ORDER BY Id"));
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(factory.ConnectionString);
        try
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<IReadOnlyList<string>> ColumnAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
