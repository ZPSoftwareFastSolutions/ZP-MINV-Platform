using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("product_barcodes", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.HasOne<BarcodeType>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BarcodeTypeId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.VariantId).IsUnique().HasFilter("is_primary");
    }
}
