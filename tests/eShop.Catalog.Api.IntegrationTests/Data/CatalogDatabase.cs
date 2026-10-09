using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.IntegrationTests.Data;

internal static class CatalogDatabase
{
    public static CatalogDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer(connectionString);
        return new CatalogDbContext(options.Options);
    }

    // EnableServiceProviderCaching(false) keeps this context off the HiLo blocks that earlier contexts in this process
    // drew, as in a new process; without it the seeder refuses the stale IDs after a migration revert.
    public static CatalogDbContext CreateContextAsInANewProcess(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer(connectionString).EnableServiceProviderCaching(false);
        return new CatalogDbContext(options.Options);
    }

    public static async Task<string> CreateMigratedAsync(SqlServerFixture sqlServer, CancellationToken cancellationToken)
    {
        var connectionString = await sqlServer.NewDatabaseAsync("migrated");
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }

    public static async Task<IReadOnlyList<string>> ItemsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(connectionString);
        var items = await context.CatalogItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        return [.. items.Select(LegacySeedData.Item)];
    }
}
