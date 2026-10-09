using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.UnitTests.Data;

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
