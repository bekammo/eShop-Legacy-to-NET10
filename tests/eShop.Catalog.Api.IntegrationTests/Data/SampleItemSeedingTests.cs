using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// The sample-item seeder that ends every Migrate (ADR-0013). Each test migrates a database of its own,
// because seeding is what they test.
[Trait("Category", "Docker")]
public sealed class SampleItemSeedingTests(SqlServerFixture sqlServer)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_seed_the_legacy_sample_items_with_their_legacy_ids()
    {
        var database = await CatalogDatabase.CreateMigratedAsync(sqlServer, CancellationToken);

        Assert.Equal(LegacySeedData.Items, await CatalogDatabase.ItemsAsync(database, CancellationToken));
    }

    [Fact]
    public async Task Seeded_items_point_at_their_legacy_brands_and_types()
    {
        var database = await CatalogDatabase.CreateMigratedAsync(sqlServer, CancellationToken);
        await using var context = CatalogDatabase.CreateContext(database);

        var items = await context.CatalogItems.AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Name + " | " + item.CatalogBrand!.Brand + " | " + item.CatalogType!.Type)
            .ToListAsync(CancellationToken);

        Assert.Equal(LegacySeedData.ItemsWithBrandAndType, items);
    }

    // The legacy seeding took two blocks of 10 for its 12 items, and left the sequence at 11.
    [Fact]
    public async Task Item_id_sequence_stands_where_the_legacy_seeding_left_it()
    {
        var database = await CatalogDatabase.CreateMigratedAsync(sqlServer, CancellationToken);

        Assert.Equal(LegacySeedData.SequenceCurrentValues["catalog_hilo"], await ItemIdSequenceAsync(database));
    }

    [Fact]
    public async Task Migrating_again_seeds_nothing()
    {
        var database = await CatalogDatabase.CreateMigratedAsync(sqlServer, CancellationToken);
        var sequence = await ItemIdSequenceAsync(database);

        await using (var context = CatalogDatabase.CreateContext(database))
        {
            await context.Database.MigrateAsync(CancellationToken);
            await context.Database.MigrateAsync(CancellationToken);
        }

        Assert.Equal(LegacySeedData.Items, await CatalogDatabase.ItemsAsync(database, CancellationToken));
        Assert.Equal(sequence, await ItemIdSequenceAsync(database));
    }

    // dotnet ef database update migrates synchronously, so it runs the synchronous seeder. The migrations run in a
    // synchronous method of their own: CA1849 allows no synchronous Migrate in an async test.
    [Fact]
    public async Task Synchronous_migration_seeds_the_same_items_once()
    {
        var database = await sqlServer.NewDatabaseAsync("seeded");

        MigrateTwice(database);

        Assert.Equal(LegacySeedData.Items, await CatalogDatabase.ItemsAsync(database, CancellationToken));
        Assert.Equal(LegacySeedData.SequenceCurrentValues["catalog_hilo"], await ItemIdSequenceAsync(database));

        static void MigrateTwice(string database)
        {
            using var context = CatalogDatabase.CreateContext(database);
            context.Database.Migrate();
            Assert.Empty(context.ChangeTracker.Entries());
            context.Database.Migrate();
        }
    }

    // EF Core keeps HiLo blocks for the life of the process, even when a revert drops the sequence and the
    // migrations create it again. The seeder refuses the old block before it draws anything from the new
    // sequence. EF Core has committed the migrations by then, and the database stays unused, so a new
    // process seeds it.
    [Fact]
    public async Task Seeding_refuses_hilo_ids_from_before_the_sequence_and_leaves_the_database_unused()
    {
        var database = await sqlServer.NewDatabaseAsync("stale");
        await using var context = CatalogDatabase.CreateContext(database);
        await context.Database.MigrateAsync(CancellationToken);
        Assert.Empty(context.ChangeTracker.Entries());
        await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.MigrateAsync(CancellationToken));

        Assert.Contains("did not get IDs 1-12", exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        Assert.Empty(await CatalogDatabase.ItemsAsync(database, CancellationToken));
        await using (var connection = await LegacyDatabase.OpenAsync(database, CancellationToken))
        {
            Assert.Equal(1, await LegacyDatabase.ScalarAsync<int>(
                connection, "SELECT COUNT(*) FROM sys.sequences WHERE name = N'catalog_hilo' AND last_used_value IS NULL", CancellationToken));
        }

        await using (var again = CatalogDatabase.CreateContextAsInANewProcess(database))
        {
            await again.Database.MigrateAsync(CancellationToken);
        }

        Assert.Equal(LegacySeedData.Items, await CatalogDatabase.ItemsAsync(database, CancellationToken));
    }

    // As in the legacy app, the process that seeded goes on with the rest of its block, from 13, and
    // every other process starts at the next block, 21. Neither can reach a seeded ID.
    [Fact]
    public async Task Items_added_after_seeding_take_ids_above_the_seeded_ones()
    {
        var database = await CatalogDatabase.CreateMigratedAsync(sqlServer, CancellationToken);
        await using var context = CatalogDatabase.CreateContext(database);

        var added = new CatalogItem { Name = "Added", PictureFileName = "dummy.png", Price = 1.50m, CatalogBrandId = 1, CatalogTypeId = 1 };
        await context.CatalogItems.AddAsync(added, CancellationToken);
        await context.SaveChangesAsync(CancellationToken);

        Assert.Equal(13, added.Id);
        await using var connection = await LegacyDatabase.OpenAsync(database, CancellationToken);
        Assert.Equal(21L, await LegacyDatabase.ScalarAsync<long>(connection, "SELECT NEXT VALUE FOR dbo.catalog_hilo", CancellationToken));
    }

    public static TheoryData<string> KeptMarkers => ["all of them", .. LegacyMarkers.Select(marker => marker.Name)];

    // An adopted legacy database must never get the sample items (ADR-0012). Each case strips the database
    // of every sign of the legacy app but one, which must stop the seeder on its own.
    [Theory]
    [MemberData(nameof(KeptMarkers))]
    public async Task Migrations_seed_nothing_into_an_adopted_legacy_database(string keptMarker)
    {
        var adopted = await LegacyDatabase.CreateAsync(sqlServer, CancellationToken);
        await using var connection = await LegacyDatabase.OpenAsync(adopted, CancellationToken);
        await LegacyDatabase.BaselineAsync(connection, CancellationToken);
        foreach (var (name, strip) in LegacyMarkers.Where(marker => keptMarker != "all of them" && marker.Name != keptMarker))
        {
            await LegacyDatabase.ExecuteAsync(connection, strip, CancellationToken);
        }

        var items = await CatalogDatabase.ItemsAsync(adopted, CancellationToken);
        var sequence = await ItemIdSequenceAsync(adopted);

        await using (var context = CatalogDatabase.CreateContext(adopted))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        Assert.Equal(items, await CatalogDatabase.ItemsAsync(adopted, CancellationToken));
        Assert.Equal(sequence, await ItemIdSequenceAsync(adopted));
    }

    // Each sign that a database is not new, with the SQL that removes it from an adopted database.
    private static readonly IReadOnlyList<(string Name, string Strip)> LegacyMarkers =
    [
        ("items", "DELETE FROM dbo.Catalog"),
        ("a used item-ID sequence", "DROP SEQUENCE dbo.catalog_hilo; CREATE SEQUENCE dbo.catalog_hilo AS bigint START WITH 1 INCREMENT BY 10"),
        ("EF6's history table", "DROP TABLE dbo.__MigrationHistory"),
        ("catalog_brand_hilo", "DROP SEQUENCE dbo.catalog_brand_hilo"),
        ("catalog_type_hilo", "DROP SEQUENCE dbo.catalog_type_hilo"),
    ];

    // sys.sequences.current_value, the value that seed-data.json records.
    private static async Task<long> ItemIdSequenceAsync(string database)
    {
        await using var connection = await LegacyDatabase.OpenAsync(database, CancellationToken);
        return await LegacyDatabase.ScalarAsync<long>(
            connection, "SELECT CAST(current_value AS bigint) FROM sys.sequences WHERE name = N'catalog_hilo'", CancellationToken);
    }
}
