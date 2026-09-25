using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasMaxLength(40);
        builder.Property(x => x.Name).HasMaxLength(150);
        builder.HasMany(x => x.Barcodes).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Barcodes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.AttributeValues).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.AttributeValues).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        builder.HasIndex(x => x.ProductId).IsUnique().HasFilter("is_default");
    }
}
