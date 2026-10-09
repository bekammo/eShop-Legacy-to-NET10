using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.UnitTests.Data;

public sealed class MigrationSnapshotTests
{
    [Fact]
    public void Model_has_no_changes_missing_from_the_migrations()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer();
        using var context = new CatalogDbContext(options.Options);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
