using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class RackConfiguration : IEntityTypeConfiguration<Rack>
{
    public void Configure(EntityTypeBuilder<Rack> builder)
    {
        builder.ToTable("racks", Schemas.Warehousing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.HasOne<Aisle>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.AisleId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AisleId, x.Code }).IsUnique();
    }
}
