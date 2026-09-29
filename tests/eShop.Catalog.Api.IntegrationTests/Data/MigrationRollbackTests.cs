using eShop.Catalog.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// The Down side of the migrations, on a database of its own: a rollback must leave nothing behind
// that stops the migrations from being applied again (ADR-0011).
public sealed class MigrationRollbackTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Migrations_revert_to_an_empty_database_and_apply_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = sqlServer.NewDatabase("rollback");
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer(connectionString);
        await using var context = new CatalogDbContext(options.Options);
        var migrator = context.GetService<IMigrator>();
        await using var connection = new SqlConnection(connectionString);

        await migrator.MigrateAsync(cancellationToken: cancellationToken);
        await migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken);

        await connection.OpenAsync(cancellationToken);
        Assert.Empty(await SqlServerSchema.TablesAsync(connection, cancellationToken));
        Assert.Empty(await SqlServerSchema.SequenceFactsAsync(connection, cancellationToken));
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(cancellationToken));

        await migrator.MigrateAsync(cancellationToken: cancellationToken);

        await LegacySchemaAssert.HasTheLegacySchemaAsync(connection, cancellationToken);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(cancellationToken));
    }
}
