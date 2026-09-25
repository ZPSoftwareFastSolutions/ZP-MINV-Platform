using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AverageCostHistoryConfiguration : IEntityTypeConfiguration<AverageCostHistory>
{
    public void Configure(EntityTypeBuilder<AverageCostHistory> builder)
    {
        builder.ToTable("average_cost_history", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_average_cost_history_costo", "average_cost >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AverageCost).HasPrecision(19, 4);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.WarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.VariantId, x.WarehouseId, x.EffectiveAt });
    }
}
