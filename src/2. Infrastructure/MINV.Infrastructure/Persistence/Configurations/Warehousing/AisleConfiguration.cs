using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AisleConfiguration : IEntityTypeConfiguration<Aisle>
{
    public void Configure(EntityTypeBuilder<Aisle> builder)
    {
        builder.ToTable("aisles", Schemas.Warehousing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.HasOne<Zone>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ZoneId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ZoneId, x.Code }).IsUnique();
    }
}
