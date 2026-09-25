using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("zones", Schemas.Warehousing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.WarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocationType>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.LocationTypeId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
    }
}
