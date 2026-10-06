using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// Contexts on a test database outside a host, with the app's SQL Server options.
internal static class CatalogDatabase
{
    public static CatalogDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer(connectionString);
        return new CatalogDbContext(options.Options);
    }

    // A context with EF Core's internal services to itself, so without the HiLo blocks that earlier contexts
    // in this process drew: it stands in for a context in a new process.
    public static CatalogDbContext CreateContextAsInANewProcess(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer(connectionString).EnableServiceProviderCaching(false);
        return new CatalogDbContext(options.Options);
    }

    // A database created and migrated the way CatalogApiFactory does it, sample items included.
    public static async Task<string> CreateMigratedAsync(SqlServerFixture sqlServer, CancellationToken cancellationToken)
    {
        var connectionString = await sqlServer.NewDatabaseAsync("migrated");
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }

    // Every item, as LegacySeedData.Items lines, in ID order.
    public static async Task<IReadOnlyList<string>> ItemsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(connectionString);
        var items = await context.CatalogItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        return [.. items.Select(LegacySeedData.Item)];
    }
}
