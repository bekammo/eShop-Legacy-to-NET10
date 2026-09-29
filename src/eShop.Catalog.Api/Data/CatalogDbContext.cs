using eShop.Catalog.Api.Catalog;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Data;

// The model reproduces the schema that the legacy EF6 app creates (docs/legacy/schema.json,
// ADR-0010): the same tables, columns, constraint names and item-ID sequence.
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    // EF6 put every object in dbo explicitly. Naming the schema keeps the tables, the sequence and
    // the migrations history there, whatever the default schema of the login that applies them.
    internal const string Schema = "dbo";

    // Item IDs come from this sequence through HiLo, in blocks of 10, as in the legacy app.
    internal const string ItemIdSequence = "catalog_hilo";

    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();

    public DbSet<CatalogBrand> CatalogBrands => Set<CatalogBrand>();

    public DbSet<CatalogType> CatalogTypes => Set<CatalogType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The history table is not part of the model: UseCatalogSqlServer puts it in the schema.
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.HasSequence<long>(ItemIdSequence)
            .StartsAt(1)
            .IncrementsBy(10);

        modelBuilder.ApplyConfiguration(new CatalogBrandConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogTypeConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogItemConfiguration());
    }
}
