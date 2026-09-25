using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductStockPolicyConfiguration : IEntityTypeConfiguration<ProductStockPolicy>
{
    public void Configure(EntityTypeBuilder<ProductStockPolicy> builder)
    {
        builder.ToTable("product_stock_policies", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_product_stock_policies_minimo", "min_quantity >= 0");
            t.HasCheckConstraint("ck_product_stock_policies_maximo", "max_quantity >= 0 AND (max_quantity = 0 OR max_quantity >= min_quantity)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MinQuantity).HasPrecision(18, 6);
        builder.Property(x => x.MaxQuantity).HasPrecision(18, 6);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.WarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.VariantId, x.WarehouseId }).IsUnique();
    }
}
