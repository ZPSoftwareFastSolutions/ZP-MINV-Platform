using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_reservations_cantidad", "quantity > 0");
            // V6 · Arco de origen: caja | línea de pedido | línea de armado (a lo sumo uno)
            t.HasCheckConstraint("ck_stock_reservations_origen", "num_nonnulls(pos_session_id, sales_order_line_id, pc_build_line_id) <= 1");
            // V7 · Dominio del estado (comprobación de normalización E13, docs/database/normalizacion-v7.md)
            t.HasCheckConstraint("ck_stock_reservations_estado", BillingChecks.In<ReservationStatus>("status"));
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<StockLevel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockLevelId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PosSession>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PosSessionId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrderLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SalesOrderLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PcBuildLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PcBuildLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StockLevelId, x.Status });
        builder.HasIndex(x => new { x.PcBuildLineId, x.Status }).HasFilter("pc_build_line_id IS NOT NULL");
    }
}
