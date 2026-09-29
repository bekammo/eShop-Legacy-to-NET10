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

        // Reference data with the IDs the legacy app assigns: GET /api/brands returns them, and
        // the sample items refer to them.
        builder.HasData(
            new CatalogBrand { Id = 1, Brand = "Azure" },
            new CatalogBrand { Id = 2, Brand = ".NET" },
            new CatalogBrand { Id = 3, Brand = "Visual Studio" },
            new CatalogBrand { Id = 4, Brand = "SQL Server" },
            new CatalogBrand { Id = 5, Brand = "Other" });
    }
}
