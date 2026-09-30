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
        builder.HasData(PreconfiguredData.CatalogTypes());
    }
}
