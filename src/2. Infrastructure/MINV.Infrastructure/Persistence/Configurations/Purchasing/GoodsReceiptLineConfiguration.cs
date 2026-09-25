using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.ToTable("goods_receipt_lines", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("ck_goods_receipt_lines_cantidad", "quantity > 0");
            t.HasCheckConstraint("ck_goods_receipt_lines_costo", "unit_cost >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.UnitCost).HasPrecision(19, 4);
        builder.HasOne<PurchaseOrderLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PurchaseOrderLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockLevel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockLevelId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.StockMovementId).IsUnique().HasFilter("stock_movement_id IS NOT NULL");
    }
}
