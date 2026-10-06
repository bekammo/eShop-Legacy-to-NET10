using eShop.Catalog.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// The idempotent script is how deployed environments apply the migrations (ADR-0011). EF Core wraps
// every step in IF NOT EXISTS, and some hand-written SQL, such as CREATE VIEW, cannot run inside
// such a block, so the script is tested as well as the migrations.
[Trait("Category", "Docker")]
public sealed class MigrationScriptTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Idempotent_script_creates_the_legacy_schema_and_can_run_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await sqlServer.NewDatabaseAsync("script");
        await using var context = CatalogDatabase.CreateContext(connectionString);
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        // The script expects an existing, empty database, which a deployment creates first, with
        // READ_COMMITTED_SNAPSHOT on as EF Core's creator does (ADR-0011).
        await context.GetService<IRelationalDatabaseCreator>().CreateAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await SqlScripts.RunAsync(connection, script, cancellationToken);
        await SqlScripts.RunAsync(connection, script, cancellationToken);

        await LegacySchemaAssert.HasTheLegacySchemaAsync(connection, cancellationToken);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(cancellationToken));
    }
}
