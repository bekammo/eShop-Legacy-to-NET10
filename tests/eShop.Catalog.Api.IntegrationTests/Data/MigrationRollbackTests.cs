using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// The Down side of the migrations, on a database of its own: a rollback must leave nothing behind
// that stops the migrations from being applied again (ADR-0011).
[Trait("Category", "Docker")]
public sealed class MigrationRollbackTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Migrations_revert_to_an_empty_database_and_apply_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await sqlServer.NewDatabaseAsync("rollback");
        await using var context = CatalogDatabase.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await using var connection = new SqlConnection(connectionString);

        await migrator.MigrateAsync(cancellationToken: cancellationToken);
        await migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken);

        await connection.OpenAsync(cancellationToken);
        Assert.Empty(await SqlServerSchema.TablesAsync(connection, cancellationToken));
        Assert.Empty(await SqlServerSchema.SequenceFactsAsync(connection, cancellationToken));
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(cancellationToken));

        // This process still holds HiLo IDs from the dropped sequence, so the seeder would refuse them
        // (ADR-0013): the migrations are applied again as a new process would apply them.
        await using (var again = CatalogDatabase.CreateContextAsInANewProcess(connectionString))
        {
            await again.Database.MigrateAsync(cancellationToken);
        }

        await LegacySchemaAssert.HasTheLegacySchemaAsync(connection, cancellationToken);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(cancellationToken));
        Assert.Equal(LegacySeedData.Items, await CatalogDatabase.ItemsAsync(connectionString, cancellationToken));
    }
}
