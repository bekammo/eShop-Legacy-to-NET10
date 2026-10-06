using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.UnitTests.Data;

// The dotnet-ef tool builds its context with this factory, without the app's host or a connection (ADR-0011), and
// generates the deployment script from it. The script must come from the app's SQL Server options, as the one that
// MigrationScriptTests runs does.
public sealed class CatalogDbContextDesignTimeFactoryTests
{
    [Fact]
    public void Deployment_script_puts_the_history_table_in_dbo()
    {
        using var context = new CatalogDbContextDesignTimeFactory().CreateDbContext([]);

        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("CREATE TABLE [dbo].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }
}
