using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.ToTable("stock_levels", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_levels_existencia", "quantity_on_hand >= 0");
            t.HasCheckConstraint("ck_stock_levels_reserva", "quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QuantityOnHand).HasPrecision(18, 6);
        builder.Property(x => x.QuantityReserved).HasPrecision(18, 6);
        builder.HasOne<Bin>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.BinId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BatchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BinId, x.BatchId }).IsUnique();
    }
}
