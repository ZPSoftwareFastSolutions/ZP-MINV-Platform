using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> builder)
    {
        builder.ToTable("stock_adjustment_lines", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_adjustment_lines_cantidad", "quantity > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.HasOne<StockLevel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockLevelId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MovementType>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.MovementTypeId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.StockMovementId).IsUnique().HasFilter("stock_movement_id IS NOT NULL");
    }
}
