using eShop.Catalog.Api.Data;
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

    // A database created and migrated the way CatalogApiFactory does it.
    public static async Task<string> CreateMigratedAsync(SqlServerFixture sqlServer, CancellationToken cancellationToken)
    {
        var connectionString = sqlServer.NewDatabase("migrated");
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }
}
