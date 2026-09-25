using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantAttributeConfiguration : IEntityTypeConfiguration<ProductVariantAttribute>
{
    public void Configure(EntityTypeBuilder<ProductVariantAttribute> builder)
    {
        builder.ToTable("product_variant_attributes", Schemas.Catalog);
        builder.HasKey(x => new { x.VariantId, x.AttributeValueId });
        builder.HasOne<CatalogAttributeValue>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AttributeValueId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
