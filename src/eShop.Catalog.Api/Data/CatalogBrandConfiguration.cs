using eShop.Catalog.Api.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eShop.Catalog.Api.Data;

internal sealed class CatalogBrandConfiguration : IEntityTypeConfiguration<CatalogBrand>
{
    public void Configure(EntityTypeBuilder<CatalogBrand> builder)
    {
        builder.ToTable("CatalogBrand");

        builder.HasKey(brand => brand.Id).HasName("PK_dbo.CatalogBrand");
        builder.Property(brand => brand.Id).UseIdentityColumn();

        builder.Property(brand => brand.Brand).HasMaxLength(100);

        builder.HasData(PreconfiguredData.CatalogBrands());
    }
}
