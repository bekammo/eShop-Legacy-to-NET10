using eShop.Catalog.Api.Catalog;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Data;

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    // Naming dbo is not redundant: it keeps the tables, the sequence and the migrations history in dbo, whatever the
    // default schema of the login that applies the migrations.
    internal const string Schema = "dbo";

    internal const string ItemIdSequence = "catalog_hilo";

    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();

    public DbSet<CatalogBrand> CatalogBrands => Set<CatalogBrand>();

    public DbSet<CatalogType> CatalogTypes => Set<CatalogType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.HasSequence<long>(ItemIdSequence)
            .StartsAt(1)
            .IncrementsBy(10);

        modelBuilder.ApplyConfiguration(new CatalogBrandConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogTypeConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogItemConfiguration());
    }
}
