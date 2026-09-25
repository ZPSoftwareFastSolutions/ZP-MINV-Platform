using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class BinAssignmentConfiguration : IEntityTypeConfiguration<BinAssignment>
{
    public void Configure(EntityTypeBuilder<BinAssignment> builder)
    {
        builder.ToTable("bin_assignments", Schemas.Warehousing);
        builder.HasKey(x => new { x.BinId, x.VariantId });
        builder.HasOne<Bin>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BinId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
