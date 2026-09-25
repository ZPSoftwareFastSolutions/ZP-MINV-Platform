using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseReturnLineConfiguration : IEntityTypeConfiguration<PurchaseReturnLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnLine> builder)
    {
        builder.ToTable("purchase_return_lines", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("ck_purchase_return_lines_cantidad", "quantity > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.HasOne<StockLevel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockLevelId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.GoodsReceiptLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
