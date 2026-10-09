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

        builder.HasData(PreconfiguredData.CatalogTypes());
    }
}
