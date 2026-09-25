using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class BinConfiguration : IEntityTypeConfiguration<Bin>
{
    public void Configure(EntityTypeBuilder<Bin> builder)
    {
        builder.ToTable("bins", Schemas.Warehousing, t =>
        {
            t.HasCheckConstraint("ck_bins_secuencia", "pick_sequence >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(40);
        builder.HasOne<Shelf>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ShelfId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocationType>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.LocationTypeId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
