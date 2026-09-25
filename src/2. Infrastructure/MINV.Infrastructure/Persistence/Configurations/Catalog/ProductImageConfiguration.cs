using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>Imagen de una variante (una por variante, en <c>bytea</c>): catálogo, stock en galería y punto de venta.</summary>
internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_product_images_tipo", "content_type IN ('image/png', 'image/jpeg')");
            t.HasCheckConstraint("ck_product_images_tamano", "octet_length(content) BETWEEN 1 AND 1048576");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(20);
        builder.Property(x => x.FileName).HasMaxLength(200);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.VariantId }).IsUnique();
    }
}
