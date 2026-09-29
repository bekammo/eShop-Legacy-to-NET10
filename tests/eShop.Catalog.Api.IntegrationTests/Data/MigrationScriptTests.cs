using System.Text.RegularExpressions;
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
public sealed partial class MigrationScriptTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Idempotent_script_creates_the_legacy_schema_and_can_run_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = sqlServer.NewDatabase("script");
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer(connectionString);
        await using var context = new CatalogDbContext(options.Options);
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        // The script expects an existing, empty database, which a deployment creates first, with
        // READ_COMMITTED_SNAPSHOT on as EF Core's creator does (ADR-0011).
        await context.GetService<IRelationalDatabaseCreator>().CreateAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await RunAsync(connection, script, cancellationToken);
        await RunAsync(connection, script, cancellationToken);

        await LegacySchemaAssert.HasTheLegacySchemaAsync(connection, cancellationToken);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(cancellationToken));
    }

    // The script separates its batches with GO, which only client tools understand.
    private static async Task RunAsync(SqlConnection connection, string script, CancellationToken cancellationToken)
    {
        foreach (var batch in BatchSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();
}
