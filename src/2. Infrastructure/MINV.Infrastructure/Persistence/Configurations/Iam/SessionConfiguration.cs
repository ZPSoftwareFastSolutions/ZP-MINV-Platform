using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_sessions_fechas", "ended_at IS NULL OR ended_at >= started_at");
            t.HasCheckConstraint("ck_sessions_token", "(token_hash IS NULL) = (expires_at IS NULL) AND (token_hash IS NULL OR token_hash ~ '^[0-9a-f]{64}$')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MachineName).HasMaxLength(100);
        builder.Property(x => x.ClientVersion).HasMaxLength(30);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HardwareToken>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HardwareTokenId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ActiveBranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.StartedAt });
        // V4 · El servidor en la nube reconoce la sesión por el hash de su token (único en la plataforma)
        builder.HasIndex(x => x.TokenHash).IsUnique().HasFilter("token_hash IS NOT NULL");
    }
}
