using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AccessLogConfiguration : IEntityTypeConfiguration<AccessLog>
{
    public void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        builder.ToTable("access_logs", Schemas.Iam);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AttemptedEmail).HasMaxLength(254);
        builder.Property(x => x.FailureReason).HasMaxLength(200);
        builder.Property(x => x.MachineName).HasMaxLength(100);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HardwareToken>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HardwareTokenId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
