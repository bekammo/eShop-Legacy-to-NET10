using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace eShop.Catalog.Api.Data;

// Used by the dotnet-ef tool instead of the app's host, so that the tool needs no configuration and
// never runs Program.cs (ADR-0011). Adding a migration or generating a script needs no connection.
// database update takes one with --connection; migrations remove cannot check the database, so it
// needs --force.
internal sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer();
        return new CatalogDbContext(options.Options);
    }
}
