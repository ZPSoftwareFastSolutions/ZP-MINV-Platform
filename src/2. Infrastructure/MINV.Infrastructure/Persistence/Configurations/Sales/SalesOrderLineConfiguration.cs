using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("sales_order_lines", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_sales_order_lines_cantidad", "quantity > 0");
            t.HasCheckConstraint("ck_sales_order_lines_precio", "unit_price >= 0");
            t.HasCheckConstraint("ck_sales_order_lines_descuento", "discount_percent >= 0 AND discount_percent <= 100");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.UnitPrice).HasPrecision(19, 4);
        builder.Property(x => x.DiscountPercent).HasPrecision(9, 4);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.StockMovementId).IsUnique().HasFilter("stock_movement_id IS NOT NULL");
    }
}
