using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_movements_cantidad", "quantity > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.DocumentReference).HasMaxLength(30);
        builder.Property(x => x.Notes).HasMaxLength(250);
        builder.Property(x => x.LegacyReference).HasMaxLength(40);
        builder.HasOne<StockLevel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockLevelId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MovementType>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.MovementTypeId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RecordedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdjustmentReason>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AdjustmentReasonId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StockLevelId, x.RecordedAt });
        builder.HasIndex(x => new { x.TenantId, x.BusinessDate });
        builder.HasIndex(x => new { x.TenantId, x.LegacyReference }).IsUnique().HasFilter("legacy_reference IS NOT NULL");
        builder.HasIndex(x => x.CorrelationId);
    }
}
