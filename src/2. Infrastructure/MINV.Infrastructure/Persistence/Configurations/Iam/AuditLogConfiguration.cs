using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_audit_logs_canal", "channel IS NULL OR channel IN ('desktop', 'cloud', 'api')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.EntityType).HasMaxLength(100);
        builder.Property(x => x.Details).HasColumnType("jsonb");
        builder.Property(x => x.LegacyReference).HasMaxLength(40);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Channel).HasMaxLength(20);
        builder.HasOne<ApiKey>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ApiKeyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.HasIndex(x => new { x.TenantId, x.ApiKeyId, x.OccurredAt }).HasFilter("api_key_id IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.LegacyReference }).IsUnique().HasFilter("legacy_reference IS NOT NULL");
    }
}
