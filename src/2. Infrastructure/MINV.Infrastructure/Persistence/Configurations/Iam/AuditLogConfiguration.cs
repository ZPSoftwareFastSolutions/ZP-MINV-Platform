using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", Schemas.Iam);
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
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.HasIndex(x => new { x.TenantId, x.LegacyReference }).IsUnique().HasFilter("legacy_reference IS NOT NULL");
    }
}
