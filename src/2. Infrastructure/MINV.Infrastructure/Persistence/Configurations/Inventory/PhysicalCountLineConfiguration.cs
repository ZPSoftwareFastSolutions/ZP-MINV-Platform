using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PhysicalCountLineConfiguration : IEntityTypeConfiguration<PhysicalCountLine>
{
    public void Configure(EntityTypeBuilder<PhysicalCountLine> builder)
    {
        builder.ToTable("physical_count_lines", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_physical_count_lines_conteo", "counted_quantity >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CountedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.SystemQuantityAtPosting).HasPrecision(18, 6);
        builder.HasOne<StockLevel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StockLevelId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CountedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PhysicalCountId, x.StockLevelId }).IsUnique();
    }
}
