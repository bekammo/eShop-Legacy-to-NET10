using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace eShop.Catalog.Api.IntegrationTests.Data;

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

        await context.GetService<IRelationalDatabaseCreator>().CreateAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await SqlScripts.RunAsync(connection, script, cancellationToken);
        await SqlScripts.RunAsync(connection, script, cancellationToken);

        await LegacySchemaAssert.HasTheLegacySchemaAsync(connection, cancellationToken);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(cancellationToken));
    }
}
