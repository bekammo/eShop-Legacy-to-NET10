using eShop.Catalog.Api.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eShop.Catalog.Api.Data;

internal sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("Catalog");

        builder.HasKey(item => item.Id).HasName("PK_dbo.Catalog");
        builder.Property(item => item.Id).UseHiLo(CatalogDbContext.ItemIdSequence);

        builder.Property(item => item.Name).HasMaxLength(50);
        builder.Property(item => item.Price).HasPrecision(18, 2);

        // Required relationships with the EF6 constraint and index names. Deleting a brand or a
        // type would delete its items, as in the legacy schema; nothing deletes either.
        builder.HasOne(item => item.CatalogBrand)
            .WithMany()
            .HasForeignKey(item => item.CatalogBrandId)
            .HasConstraintName("FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => item.CatalogBrandId).HasDatabaseName("IX_CatalogBrandId");

        builder.HasOne(item => item.CatalogType)
            .WithMany()
            .HasForeignKey(item => item.CatalogTypeId)
            .HasConstraintName("FK_dbo.Catalog_dbo.CatalogType_CatalogTypeId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => item.CatalogTypeId).HasDatabaseName("IX_CatalogTypeId");
    }
}
