using eShop.Catalog.Api.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eShop.Catalog.Api.Data;

internal sealed class CatalogTypeConfiguration : IEntityTypeConfiguration<CatalogType>
{
    public void Configure(EntityTypeBuilder<CatalogType> builder)
    {
        builder.ToTable("CatalogType");

        builder.HasKey(type => type.Id).HasName("PK_dbo.CatalogType");
        builder.Property(type => type.Id).UseIdentityColumn();

        builder.Property(type => type.Type).HasMaxLength(100);

        // Reference data with the IDs the legacy app assigns: the sample items refer to them.
        builder.HasData(
            new CatalogType { Id = 1, Type = "Mug" },
            new CatalogType { Id = 2, Type = "T-Shirt" },
            new CatalogType { Id = 3, Type = "Sheet" },
            new CatalogType { Id = 4, Type = "USB Memory Stick" });
    }
}
