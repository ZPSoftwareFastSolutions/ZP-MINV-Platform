using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class HardwareTokenConfiguration : IEntityTypeConfiguration<HardwareToken>
{
    public void Configure(EntityTypeBuilder<HardwareToken> builder)
    {
        builder.ToTable("hardware_tokens", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_hardware_tokens_revocacion", "revoked_at IS NULL OR revoked_at >= registered_at");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Fingerprint).HasMaxLength(128);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Fingerprint }).IsUnique();
    }
}
